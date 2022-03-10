#region Namespaces
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB.Structure;
using System.Windows.Forms;
#endregion

namespace BeamCasing_ButtonCreate
{
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    class CreateBeamCast : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            //先設置要進行轉換的單位
            DisplayUnitType unitType = DisplayUnitType.DUT_MILLIMETERS;
            try
            {
                UIApplication uiapp = commandData.Application;
                Autodesk.Revit.ApplicationServices.Application app = uiapp.Application;

                UIDocument uidoc = commandData.Application.ActiveUIDocument;
                ISelectionFilter pipefilter = new PipeSelectionFilter();
                Document doc = uidoc.Document;

                //點選要放置穿樑套管的管段
                Reference pickElements_refer = uidoc.Selection.PickObject(ObjectType.Element, pipefilter, $"請選擇欲放置穿樑套管的管段");
                Element pickPipe = doc.GetElement(pickElements_refer.ElementId);


                //點選要取交集的外參樑
                List<Element> pickBeams = new List<Element>(); //選取一個容器放置外參樑
                ISelectionFilter beamFilter = new BeamsLinkedSelectedFilter(doc);
                IList<Reference> refElems_Linked = uidoc.Selection.PickObjects(ObjectType.LinkedElement, beamFilter, $"請選擇穿過的樑，可多選");
                foreach (Reference refer in refElems_Linked)
                {
                    RevitLinkInstance revitLinkInstance = doc.GetElement(refer) as RevitLinkInstance;
                    Autodesk.Revit.DB.Document docLink = revitLinkInstance.GetLinkDocument();
                    Element eBeamsLinked = docLink.GetElement(refer.LinkedElementId);
                    pickBeams.Add(eBeamsLinked);
                }

                //得到外參樑的revitLinkInstance來判斷transform


                Family RC_Cast;

                //載入元件檔
                using (Transaction tx = new Transaction(doc))
                {
                    tx.Start("載入檔案測試");

                    RC_Cast = new BeamCast().BeamCastSymbol(doc);
                    tx.Commit();
                }

                //抓到模型中所有的樓層元素，依照樓高排序。要找到位於他上方的樓層
                FilteredElementCollector levelCollector = new FilteredElementCollector(doc);
                ElementFilter level_Filter = new ElementCategoryFilter(BuiltInCategory.OST_Levels);
                levelCollector.WherePasses(level_Filter).WhereElementIsNotElementType().ToElements();

                string output = "";
                List<string> levelNames = new List<string>(); //用名字來確認篩選排序
                MEPCurve pipeCrv = pickPipe as MEPCurve;
                Level lowLevel = pipeCrv.ReferenceLevel; //管在的樓層為下樓層
                List<Element> level_List = levelCollector.OrderBy(x => sortLevelbyHeight(x)).ToList();

                for (int i = 0; i < level_List.Count(); i++)
                {
                    Level le = level_List[i] as Level;
                    levelNames.Add(le.Name);
                }

                //利用index反查樓層的位置，就可以用這個方式反推他的上一個樓層
                int index_lowLevel = levelNames.IndexOf(lowLevel.Name);
                int index_topLevel = index_lowLevel + 1;
                Level topLevel = null;

                if (index_topLevel < level_List.Count())
                {
                    topLevel = level_List[index_topLevel] as Level;
                }
                else if(topLevel == null)
                {
                    message = "管的上方沒有樓層，無法計算穿樑套管偏移值";
                    return Result.Failed;
                }

                //尋找連結模型中的元素_方法2
                IList<FamilyInstance> CastList = new List<FamilyInstance>(); //創造一個裝每次被創造出來的familyinstance的容器，用來以bounding box計算bop&top
                FamilyInstance instance = null;
                int intersectCount = 0;
                double intersectLength = 0;
                int totalIntersectCount = 0;

                using (Transaction trans = new Transaction(doc))
                {
                    trans.Start("放置穿樑套管");
                    foreach (Element e in pickBeams)
                    {
                        Solid solid = singleSolidFromElement(e);
                        if (null != solid)
                        {
                            SolidCurveIntersectionOptions options = new SolidCurveIntersectionOptions();
                            LocationCurve locationCurve = pickPipe.Location as LocationCurve;
                            Curve pipeCurve = locationCurve.Curve;
                            SolidCurveIntersection intersection = solid.IntersectWithCurve(pipeCurve, options);
                            intersectCount = intersection.SegmentCount;
                            totalIntersectCount += intersectCount;

                            ////選擇完外參樑後，建立一個List裝取所有在這跟外參樑中的套管
                            //List<Element> castsInThisBeam = otherCast(doc, solid);


                            for (int i = 0; i < intersectCount; i++)
                            {
                                intersectLength += intersection.GetCurveSegment(i).Length;
                                Curve tempCurve = intersection.GetCurveSegment(i);
                                XYZ tempCenter = tempCurve.Evaluate(0.5, true);
                                double tempStart = tempCurve.GetEndPoint(0).Z;
                                double tempEnd = tempCurve.GetEndPoint(1).Z;
                                //MessageBox.Show($"{tempStart}&{tempEnd}&{tempCenter.Z}");
                                FamilySymbol CastSymbol2 = new BeamCast().findRC_CastSymbol(doc, RC_Cast, pickPipe);
                                //tempCenter = new XYZ(tempCenter.X, tempCenter.Y, 0);
                                ////針對找到的FamilySymbol去尋找是否有遺漏的參數
                                //double adjust2 = 0.0;
                                //if (tempStart > tempEnd)
                                //{
                                //    adjust2 = tempStart - tempCenter.Z;
                                //}else if (tempStart < tempEnd)
                                //{
                                //    adjust2 = tempEnd - tempCenter.Z;
                                //}



                                instance = doc.Create.NewFamilyInstance(tempCenter, CastSymbol2, topLevel, StructuralType.NonStructural);

                                List<string> paraNameToCheck = new List<string>()
                                {
                                   "L","系統別","TTOP","TCOP","TBOP","BBOP","BCOP","BTOP"
                                };

                                foreach (string item in paraNameToCheck)
                                {
                                    if (!checkPara(instance, item))
                                    {
                                        MessageBox.Show($"執行失敗，請檢查{instance.Symbol.FamilyName}元件中是否缺少{item}參數欄位");
                                        return Result.Failed;
                                    }
                                }




                                //調整長度與高度
                                instance.LookupParameter("L").Set(intersection.GetCurveSegment(i).Length + 2 / 30.48); //套管前後加兩公分
                                double floorHeight = topLevel.Elevation - lowLevel.Elevation;
                                double adjust = instance.LookupParameter("管外半徑").AsDouble();
                                double toMove2 = tempCenter.Z - topLevel.Elevation + adjust;
                                //double toMove = tempCenter.Z-floorHeight + adjust;
                                //double toMove = instance.LookupParameter("偏移").AsDouble() + adjust;
                                //double toMove = pickPipe.LookupParameter("偏移").AsDouble() + adjust;
                                double toMove = pickPipe.LookupParameter("偏移").AsDouble() - floorHeight + adjust;
                                instance.LookupParameter("偏移").Set(toMove2);

                                //旋轉角度
                                XYZ axisPt1 = new XYZ(tempCenter.X, tempCenter.Y, tempCenter.Z);
                                XYZ axisPt2 = new XYZ(tempCenter.X, tempCenter.Y, tempCenter.Z + 1);
                                XYZ basePoint = new XYZ(0, tempCenter.Y, 0);
                                Line Axis = Line.CreateBound(axisPt1, axisPt2);

                                XYZ projectStart = intersection.GetCurveSegment(i).GetEndPoint(0);
                                XYZ projectEnd = intersection.GetCurveSegment(i).GetEndPoint(1);
                                XYZ projectEndAdj = new XYZ(projectEnd.X, projectEnd.Y, projectStart.Z);



                                //重新以樑的locationCurve 計算旋轉角度
                                LocationCurve beamLocate = e.Location as LocationCurve;
                                Curve beamCurve = beamLocate.Curve;
                                XYZ beamStart = beamCurve.GetEndPoint(0);
                                XYZ beamEnd = beamCurve.GetEndPoint(1);
                                beamEnd = new XYZ(beamEnd.X, beamEnd.Y, beamStart.Z);
                                Line beamCrvProject = Line.CreateBound(beamStart,beamEnd);

                                Line intersectProject = Line.CreateBound(projectStart, projectEndAdj);
                                double degree = 0.0;
                                //degree = basePoint.AngleTo(intersectProject.Direction);
                                degree = -basePoint.AngleTo(beamCrvProject.Direction)-Math.PI/2;
                                double degree2 = intersectProject.Direction.AngleTo(beamCrvProject.Direction) * 180 / Math.PI;
                                double degreeCheck = Math.Round(degree2, 0);
                                instance.Location.Rotate(Axis, degree);


                                #region 為了要讓電管也可以使用，把自動放置就寫入系統別的功能拿掉。
                                ////寫入系統別
                                //string pipeSystem = pickPipe.LookupParameter("系統類型").AsValueString();
                                //Parameter instSystem = instance.LookupParameter("系統別");
                                //if (pipeSystem.Contains("P 排水"))
                                //{
                                //    instSystem.Set("P");
                                //}
                                //else if (pipeSystem.Contains("P 通風"))
                                //{
                                //    instSystem.Set("P");
                                //}
                                //else if (pipeSystem.Contains("E 電氣"))
                                //{
                                //    instSystem.Set("E");
                                //}
                                //else if (pipeSystem.Contains("M 空調水"))
                                //{
                                //    instSystem.Set("A");
                                //}
                                //else if (pipeSystem.Contains("F 消防"))
                                //{
                                //    instSystem.Set("F");
                                //}
                                //else if (pipeSystem.Contains("W 給水"))
                                //{
                                //    instSystem.Set("W");
                                //}
                                //else if (pipeSystem.Contains("G 瓦斯"))
                                //{
                                //    instSystem.Set("G");
                                //}
                                //else
                                //{
                                //    instSystem.Set("未指定");
                                //}
                                #endregion

                                //針對已在樑中的穿樑套管做檢核
                                double casrCreatedWidth = instance.get_BoundingBox(null).Max.Z - instance.get_BoundingBox(null).Min.Z;
                                LocationPoint castCreatedLocate = instance.Location as LocationPoint;
                                XYZ castCreatedXYZ = castCreatedLocate.Point;


                                #region 在放置時就檢查使否過近的功能，目前暫不需要
                                //if (castsInThisBeam.Count() > 0)
                                //{
                                //    foreach (Element cast in castsInThisBeam)
                                //    {
                                //        //取得這個在樑中套管的「寬度」
                                //        double castWidth = cast.get_BoundingBox(null).Max.Z - cast.get_BoundingBox(null).Min.Z;
                                //        LocationPoint locatePt = cast.Location as LocationPoint;
                                //        XYZ locateXYZ = locatePt.Point;

                                //        //調整每個穿樑套管的點位到與正在創造的這個至同樣高度後，測量距離
                                //        XYZ locateAdjust = new XYZ(locateXYZ.X, locateXYZ.Y, castCreatedXYZ.Z);
                                //        double distBetween = castCreatedXYZ.DistanceTo(locateAdjust);

                                //        //如果水平向距離太近，則無法放置穿樑套管
                                //        if (distBetween < (casrCreatedWidth + castWidth) * 1.5)
                                //        {
                                //            elements.Insert(cast);
                                //            message = "管離亮顯的套管太近，無法放置穿樑套管";
                                //            return Result.Failed;
                                //        }

                                //    }
                                //}
                                #endregion

                                //設定BOP、TOP
                                if (intersectCount > 0)
                                {
                                    Solid tempBeam = solid; //如果樑有切割到，則對樑進行計算
                                    XYZ tempBeam_Max = tempBeam.GetBoundingBox().Max;
                                    XYZ tempBeam_Min = tempBeam.GetBoundingBox().Min;

                                    XYZ instance_Max = instance.get_BoundingBox(null).Max;
                                    XYZ instance_Min = instance.get_BoundingBox(null).Min;
                                    double instanceHeight = instance_Max.Z - instance_Min.Z; //穿樑套管的高度

                                    //針對每個實體
                                    XYZ tempCenter_Up = new XYZ(tempCenter.X, tempCenter.Y, tempCenter.Z + 50);
                                    XYZ tempCenter_Dn = new XYZ(tempCenter.X, tempCenter.Y, tempCenter.Z - 50);
                                    Curve vertiaclLine = Line.CreateBound(tempCenter_Dn, tempCenter_Up);

                                    SolidCurveIntersection castIntersect = solid.IntersectWithCurve(vertiaclLine, options);
                                    Curve castIntersect_Crv = castIntersect.GetCurveSegment(0);
                                    XYZ intersect_DN = castIntersect_Crv.GetEndPoint(0);
                                    XYZ intersect_UP = castIntersect_Crv.GetEndPoint(1);

                                    double castCenter_Z = (instance_Max.Z + instance_Min.Z) / 2;
                                    double TTOP = intersect_UP.Z - instance_Max.Z;
                                    double BTOP = instance_Max.Z - intersect_DN.Z;
                                    double TCOP = intersect_UP.Z - castCenter_Z;
                                    double BCOP = castCenter_Z - intersect_DN.Z;
                                    double TBOP = intersect_UP.Z - instance_Min.Z;
                                    double BBOP = instance_Min.Z - intersect_DN.Z; ;

                                    instance.LookupParameter("TTOP").Set(TTOP);
                                    instance.LookupParameter("BTOP").Set(BTOP);
                                    instance.LookupParameter("TCOP").Set(TCOP);
                                    instance.LookupParameter("BCOP").Set(BCOP);
                                    instance.LookupParameter("TBOP").Set(TBOP);
                                    instance.LookupParameter("BBOP").Set(BBOP);


                                    //設定1/4的穿樑原則警告
                                    double beamHeight = castIntersect_Crv.Length; //樑在那個斷面的高度
                                    double alertValue = beamHeight / 5;
                                    double min_alertValue = UnitUtils.ConvertToInternalUnits(200, unitType);//除了1/4或1/3的保護層限制之外，也有最小保護層的限制
                                    if (alertValue < min_alertValue)
                                    {
                                        alertValue = min_alertValue;
                                    }

                                    #region 在放置時就檢查使否過近的功能，目前暫不需要
                                    //if (instanceHeight > beamHeight / 3)
                                    //{
                                    //    //設定如果穿樑套管>該樑斷面的1/3，則無法放置穿樑套管
                                    //    message = "樑的斷面尺寸過小，無法放置穿樑套管，請將亮顯的管線改走樑下";
                                    //    elements.Insert(pickPipe);
                                    //    return Result.Failed;
                                    //}
                                    //else if (BBOP < alertValue)
                                    //{
                                    //    message = "管離「樑底部」過近，請調整後重新放置穿樑套管";
                                    //    //MessageBox.Show("管離「樑底部」過近，請調整後重新放置穿樑套管");
                                    //    elements.Insert(pickPipe);
                                    //    return Result.Failed;
                                    //}
                                    //else if (TTOP < alertValue)
                                    //{
                                    //    message = "管離「樑頂部」過近，請調整後重新放置穿樑套管";
                                    //    //MessageBox.Show("管離「樑頂部」過近，請調整後重新放置穿樑套管");
                                    //    elements.Insert(pickPipe);
                                    //    return Result.Failed;
                                    //}
                                    #endregion
                                }
                            }
                        }
                    }

                    if (totalIntersectCount == 0)
                    {
                        message = "管沒有和任何的樑交集，請重新調整!";

                        elements.Insert(pickPipe);
                        return Result.Failed;
                    }
                    trans.Commit();
                }
            }
            catch
            {
                MessageBox.Show("執行失敗喔!");
                return Result.Failed;
            }
            return Result.Succeeded;
        }

        class BeamCast
        {
            //將穿樑套管設置為class，需要符合下列幾種功能
            //1.先匯入我們目前所有的穿樑套管
            //2.再來判斷選中的管徑與是否有穿過梁，以及穿過的樑種類
            //3.如果有則利用穿過的部分為終點，創造穿樑套管與輸入長度
            //public Family BeamCastSymbol(Element pipe, Document doc)

            //載入RC穿樑套管元件
            public Family BeamCastSymbol(Document doc)
            {
                //尋找RC樑開口.rfa
                string internalNameRC = "CEC-穿樑套管-圓";
                //string RC_CastName = "穿樑套管共用參數_通用模型";
                Family RC_CastType = null;
                ElementFilter RC_CastCategoryFilter = new ElementCategoryFilter(BuiltInCategory.OST_PipeAccessory);
                ElementFilter RC_CastSymbolFilter = new ElementClassFilter(typeof(FamilySymbol));

                LogicalAndFilter andFilter = new LogicalAndFilter(RC_CastCategoryFilter, RC_CastSymbolFilter);
                FilteredElementCollector RC_CastSymbol = new FilteredElementCollector(doc);
                RC_CastSymbol = RC_CastSymbol.WherePasses(andFilter);//這地方有點怪，無法使用andFilter RC_CastSymbolFilter
                bool symbolFound = false;
                foreach (FamilySymbol e in RC_CastSymbol)
                {
                    Parameter p = e.LookupParameter("API識別名稱");
                    if (p != null && p.AsString().Contains(internalNameRC))
                    {
                        symbolFound = true;
                        RC_CastType = e.Family;
                        break;
                    }
                }
                if (!symbolFound)
                {
                    MessageBox.Show("尚未載入指定的穿樑套管元件!");
                }

                ////如果沒有找到，則自己加載
                //if (!symbolFound)
                //{
                //    string filePath = @"D:\Dropbox (CHC Group)\工作人生\組內專案\04.元件製作\穿樑套管\穿樑套管共用參數_通用模型.rfa";
                //    Family family;
                //    bool loadSuccess = doc.LoadFamily(filePath, out family);
                //    if (loadSuccess)
                //    {
                //        RC_CastType = family;
                //    }
                //}

                return RC_CastType;
            }

            //根據不同的管徑，選擇不同的穿樑套管大小
            public FamilySymbol findRC_CastSymbol(Document doc, Family CastFamily, Element element)
            {
                FamilySymbol targetFamilySymbol = null; //用來找目標familySymbol
                                                        //如果確定找到family後，針對不同得管選取不同的穿樑套管大小，以大兩吋為規則，如果有坡度則大三吋
                Parameter targetPara = null;
                if (element.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM) != null)
                {
                    targetPara = element.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                }
                else if (element.get_Parameter(BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM) != null)
                {
                    targetPara = element.get_Parameter(BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM);
                }

                if (CastFamily != null)
                {
                    foreach (ElementId castId in CastFamily.GetFamilySymbolIds())
                    {
                        FamilySymbol tempSymbol = doc.GetElement(castId) as FamilySymbol;
                        if (targetPara.AsValueString() == "50 mm")
                        {
                            if (tempSymbol.Name == "80mm")
                            {
                                targetFamilySymbol = tempSymbol;
                            }
                        }
                        else if (targetPara.AsValueString() == "65 mm")
                        {
                            if (tempSymbol.Name == "100mm")
                            {
                                targetFamilySymbol = tempSymbol;
                            }
                        }
                        else if (targetPara.AsValueString() == "80 mm")
                        {
                            if (tempSymbol.Name == "125mm")
                            {
                                targetFamilySymbol = tempSymbol;
                            }
                        }
                        else if (targetPara.AsValueString() == "100 mm")
                        {
                            if (tempSymbol.Name == "150mm")
                            {
                                targetFamilySymbol = tempSymbol;
                            }
                        }

                        else if (targetPara.AsValueString() == "125 mm")
                        {
                            if (tempSymbol.Name == "150mm")
                            {
                                targetFamilySymbol = tempSymbol;
                            }
                        }
                        else if (targetPara.AsValueString() == "150 mm")
                        {
                            if (tempSymbol.Name == "200mm")
                            {
                                targetFamilySymbol = tempSymbol;
                            }
                        }
                        else if (targetPara.AsValueString() == "200 mm")
                        {
                            if (tempSymbol.Name == "250mm")
                            {
                                targetFamilySymbol = tempSymbol;
                            }
                        }
                        else if (targetPara.AsValueString() == "250 mm")
                        {
                            if (tempSymbol.Name == "300mm")
                            {
                                targetFamilySymbol = tempSymbol;
                            }
                        }
                        else
                        {
                            if (tempSymbol.Name == "80mm")
                            {
                                targetFamilySymbol = tempSymbol;
                            }
                        }
                    }
                }
                targetFamilySymbol.Activate();
                return targetFamilySymbol;
            }
        }

        public List<Element> otherCast(Document doc, Solid solid)
        {
            List<Element> castIntersected = new List<Element>();
            FilteredElementCollector otherCastCollector = new FilteredElementCollector(doc);
            ElementFilter RC_CastFilter = new ElementCategoryFilter(BuiltInCategory.OST_PipeAccessory);
            ElementFilter Cast_InstFilter = new ElementClassFilter(typeof(FamilyInstance));
            LogicalAndFilter andFilter = new LogicalAndFilter(RC_CastFilter, Cast_InstFilter);
            Outline beamOtLn = new Outline(solid.GetBoundingBox().Min, solid.GetBoundingBox().Min);
            //BoundingBoxIntersectsFilter beamBoundingBoxFilter = new BoundingBoxIntersectsFilter(beamOtLn);
            otherCastCollector.WherePasses(andFilter).WhereElementIsNotElementType();
            //otherCastCollector.WherePasses(beamBoundingBoxFilter).ToElements();
            otherCastCollector.WherePasses(new ElementIntersectsSolidFilter(solid));
            foreach (FamilyInstance e in otherCastCollector)
            {
                if (e.Symbol.FamilyName == "穿樑套管共用參數_通用模型")
                {
                    castIntersected.Add(e);
                }
            }
            return castIntersected;
        }

        //製作用來排序樓層的方法
        public double sortLevelbyHeight(Element element)
        {
            Level tempLevel = element as Level;
            double levelHeight = element.LookupParameter("立面").AsDouble();
            return levelHeight;
        }

        //建立管過濾器
        public class PipeSelectionFilter : Autodesk.Revit.UI.Selection.ISelectionFilter
        {
            public bool AllowElement(Element element)
            {
                if (element.Category.Name == "管"|| element.Category.Name == "電管")
                {
                    return true;
                }
                return false;
            }
            public bool AllowReference(Reference refer, XYZ point)
            {
                return false;
            }
        }

        //建立外參樑過濾器
        public class BeamsLinkedSelectedFilter : ISelectionFilter
        {
            Autodesk.Revit.DB.Document doc = null;

            public BeamsLinkedSelectedFilter(Document document)
            {
                doc = document;
            }
            public bool AllowElement(Element element)
            {
                return true;
            }
            public bool AllowReference(Reference reference, XYZ point)
            {
                RevitLinkInstance revitLinkInstance = doc.GetElement(reference) as RevitLinkInstance;
                Autodesk.Revit.DB.Document docLink = revitLinkInstance.GetLinkDocument();
                Element eBeamsLink = docLink.GetElement(reference.LinkedElementId);
                if (eBeamsLink.Category.Name == "結構構架")
                {
                    return true;
                }
                return false;
            }
        }
        public IList<Solid> GetTargetSolids(Element element)
        {
            List<Solid> solids = new List<Solid>();
            Options options = new Options();
            //預設為不包含不可見元件，因此改成true
            options.ComputeReferences = true;
            options.DetailLevel = ViewDetailLevel.Fine;
            options.IncludeNonVisibleObjects = true;
            GeometryElement geomElem = element.get_Geometry(options);
            foreach (GeometryObject geomObj in geomElem)
            {
                if (geomObj is Solid)
                {
                    Solid solid = (Solid)geomObj;
                    if (solid.Faces.Size > 0 && solid.Volume > 0.0)
                    {
                        solids.Add(solid);
                    }
                }
                else if (geomObj is GeometryInstance)//一些特殊狀況可能會用到，like樓梯
                {
                    GeometryInstance geomInst = (GeometryInstance)geomObj;
                    GeometryElement instGeomElem = geomInst.GetInstanceGeometry();
                    foreach (GeometryObject instGeomObj in instGeomElem)
                    {
                        if (instGeomObj is Solid)
                        {
                            Solid solid = (Solid)instGeomObj;
                            if (solid.Faces.Size > 0 && solid.Volume > 0.0)
                            {
                                solids.Add(solid);
                            }
                        }
                    }
                }
            }
            return solids;
        }
        public Solid singleSolidFromElement(Element inputElement)
        {
            Document doc = inputElement.Document;
            Autodesk.Revit.ApplicationServices.Application app = doc.Application;
            // create solid from Element:
            IList<Solid> fromElement = GetTargetSolids(inputElement);
            int solidCount = fromElement.Count;
            // MessageBox.Show(solidCount.ToString());
            // Merge all found solids into single one
            Solid solidResult = null;
            //XYZ checkheight = new XYZ(0, 0, 6.88976);
            //Transform tr = Transform.CreateTranslation(checkheight);
            if (solidCount == 1)
            {
                solidResult = fromElement[0];
            }
            else if (solidCount > 1)
            {
                solidResult =
                    BooleanOperationsUtils.ExecuteBooleanOperation(fromElement[0], fromElement[1], BooleanOperationsType.Union);
            }

            if (solidCount > 2)
            {
                for (int i = 2; i < solidCount; i++)
                {
                    solidResult = BooleanOperationsUtils.ExecuteBooleanOperation(solidResult, fromElement[i], BooleanOperationsType.Union);
                }
            }
            return solidResult;
        }
        public bool checkPara(Element elem, string paraName)
        {
            bool result = false;
            foreach (Parameter parameter in elem.Parameters)
            {
                Parameter val = parameter;
                if (val.Definition.Name == paraName)
                {
                    result = true;
                }
            }
            return result;
        }
    }
}
