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
    class BeamCasting_selectTestV2 : IExternalCommand
    {
        //點選管創建穿樑套管，通用模型元件測試
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

                Family RC_Cast;

                //載入元件檔
                using (Transaction tx = new Transaction(doc))
                {
                    tx.Start("載入檔案測試");

                    RC_Cast = new BeamCast().BeamCastSymbol(doc);
                    //MessageBox.Show($"找到的元件名稱為:{RC_Cast.Name}");
                    tx.Commit();
                }

                //根據不同管徑選擇不同大小的套管
                using (Transaction tx = new Transaction(doc))
                {
                    tx.Start("尋找元件");
                    Element CastSymbol = new BeamCast().findRC_CastSymbol(doc, RC_Cast, pickPipe);
                    //MessageBox.Show($"找到的元件名稱為:{CastSymbol.Name}");
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
                    //output += $"我是集合中的第{i}個樓層，我的名字是{le.Name}，我的高度是{le.LookupParameter("立面").AsValueString()}\n";
                }
                //利用index反查樓層的位置，就可以用這個方式反推他的上一個樓層
                int index_lowLevel = levelNames.IndexOf(lowLevel.Name);
                int index_topLevel = index_lowLevel + 1;
                Level topLevel = level_List[index_topLevel] as Level;

                if (index_topLevel > level_List.Count())
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
                        Options geomOptions = new Options();
                        geomOptions.ComputeReferences = true;
                        geomOptions.DetailLevel = ViewDetailLevel.Fine;

                        //GeometryElement類中包含了構成該園件的所有幾何元素，線、邊、面、體-->這些在Revit中被歸為GeometryObject類
                        //因此要用foreach找出其中屬於solid的部分
                        GeometryElement geoElement = e.get_Geometry(geomOptions);
                        foreach (GeometryObject obj in geoElement)
                        {
                            Solid solid = obj as Solid;
                            if (null != solid)
                            {
                                SolidCurveIntersectionOptions options = new SolidCurveIntersectionOptions();
                                LocationCurve locationCurve = pickPipe.Location as LocationCurve;
                                Curve pipeCurve = locationCurve.Curve;
                                SolidCurveIntersection intersection = solid.IntersectWithCurve(pipeCurve, options);
                                intersectCount = intersection.SegmentCount;
                                totalIntersectCount += intersectCount;

                                //選擇完外參樑後，建立一個List裝取所有在這跟外參樑中的套管
                                List<Element> castsInThisBeam = otherCast(doc, solid);
                                //List<Element> castsInThisBeam_elem = otherCast_elem(doc, e);
                                //MessageBox.Show($"在這根樑中有{castsInThisBeam_elem.Count()}個套管了_boundingBOX版");


                                for (int i = 0; i < intersectCount; i++)
                                {
                                    intersectLength += intersection.GetCurveSegment(i).Length;
                                    Curve tempCurve = intersection.GetCurveSegment(i);
                                    XYZ tempCenter = tempCurve.Evaluate(0.5, true);
                                    FamilySymbol CastSymbol2 = new BeamCast().findRC_CastSymbol(doc, RC_Cast, pickPipe);


                                    //開始放置穿樑套管
                                    //using (Transaction tx = new Transaction(doc))
                                    //{
                                    //    tx.Start("創造穿樑套管");
                                    //創建穿樑套管
                                    instance = doc.Create.NewFamilyInstance(tempCenter, CastSymbol2, topLevel, StructuralType.NonStructural);

                                    //調整長度與高度
                                    instance.LookupParameter("長度").Set(intersection.GetCurveSegment(i).Length + 2 / 30.48); //套管前後加兩公分
                                    double floorHeight = topLevel.Elevation - lowLevel.Elevation;
                                    //double instHeight = instance.get_BoundingBox(null).Max.Z - instance.get_BoundingBox(null).Min.Z;
                                    //double toMove = pickPipe.LookupParameter("偏移").AsDouble() - floorHeight+ pickPipe.LookupParameter("直徑").AsDouble();
                                    double adjust = instance.LookupParameter("管外半徑").AsDouble();
                                    double toMove = pickPipe.LookupParameter("偏移").AsDouble() - floorHeight + adjust;
                                    double toMove2 = tempCenter.Z - topLevel.Elevation + pickPipe.LookupParameter("直徑").AsDouble();
                                    instance.LookupParameter("偏移").Set(toMove);


                                    //旋轉角度
                                    XYZ axisPt1 = new XYZ(tempCenter.X, tempCenter.Y, tempCenter.Z);
                                    XYZ axisPt2 = new XYZ(tempCenter.X, tempCenter.Y, tempCenter.Z + 1);
                                    XYZ basePoint = new XYZ(0, tempCenter.Y, 0);
                                    Line Axis = Line.CreateBound(axisPt1, axisPt2);

                                    XYZ projectStart = intersection.GetCurveSegment(i).GetEndPoint(0);
                                    XYZ projectEnd = intersection.GetCurveSegment(i).GetEndPoint(1);
                                    XYZ projectEndAdj = new XYZ(projectEnd.X, projectEnd.Y, projectStart.Z);

                                    //Line intersectLine = intersection.GetCurveSegment(i) as Line;
                                    Line intersectProject = Line.CreateBound(projectStart, projectEndAdj);
                                    double degree = 0.0;
                                    degree = basePoint.AngleTo(intersectProject.Direction);
                                    instance.Location.Rotate(Axis, degree);

                                    //寫入系統別
                                    string pipeSystem = pickPipe.LookupParameter("系統類型").AsValueString();
                                    if (pipeSystem.Contains("P 排水"))
                                    {
                                        instance.LookupParameter("系統別").Set("P");
                                    }
                                    else if (pipeSystem.Contains("P 通風"))
                                    {
                                        instance.LookupParameter("系統別").Set("P");
                                    }
                                    else if (pipeSystem.Contains("E 電氣"))
                                    {
                                        instance.LookupParameter("系統別").Set("E");
                                    }
                                    else if (pipeSystem.Contains("M 空調水"))
                                    {
                                        instance.LookupParameter("系統別").Set("A");
                                    }
                                    else if (pipeSystem.Contains("F 消防"))
                                    {
                                        instance.LookupParameter("系統別").Set("F");
                                    }
                                    else if (pipeSystem.Contains("W 給水"))
                                    {
                                        instance.LookupParameter("系統別").Set("W");
                                    }
                                    else if (pipeSystem.Contains("G 瓦斯"))
                                    {
                                        instance.LookupParameter("系統別").Set("G");
                                    }
                                    else
                                    {
                                        instance.LookupParameter("系統別").Set("未指定");
                                    }


                                    //可以用這樣的方法取的穿樑套管的外徑
                                    //MessageBox.Show($"這個穿樑套管的管外徑為{(instance.get_BoundingBox(null).Max.Z-instance.get_BoundingBox(null).Min.Z)*30.48}cm");

                                    //針對已在樑中的穿樑套管做檢核
                                    double casrCreatedWidth = instance.get_BoundingBox(null).Max.Z - instance.get_BoundingBox(null).Min.Z;
                                    LocationPoint castCreatedLocate = instance.Location as LocationPoint;
                                    XYZ castCreatedXYZ = castCreatedLocate.Point;

                                    if (castsInThisBeam.Count() > 0)
                                    {
                                        foreach (Element cast in castsInThisBeam)
                                        {
                                            //取得這個在樑中套管的「寬度」
                                            double castWidth = cast.get_BoundingBox(null).Max.Z - cast.get_BoundingBox(null).Min.Z;
                                            LocationPoint locatePt = cast.Location as LocationPoint;
                                            XYZ locateXYZ = locatePt.Point;

                                            //調整每個穿樑套管的點位到與正在創造的這個至同樣高度後，測量距離
                                            XYZ locateAdjust = new XYZ(locateXYZ.X, locateXYZ.Y, castCreatedXYZ.Z);
                                            double distBetween = castCreatedXYZ.DistanceTo(locateAdjust);

                                            //如果水平向距離太近，則無法放置穿樑套管
                                            if (distBetween < (casrCreatedWidth + castWidth) * 1.5)
                                            {
                                                elements.Insert(cast);
                                                message = "管離亮顯的套管太近，無法放置穿樑套管";
                                                return Result.Failed;
                                            }

                                        }
                                    }

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
                                        //MessageBox.Show($"{alertValue}");
                                        //MessageBox.Show(min_alertValue.ToString());


                                        if (instanceHeight > beamHeight / 3)
                                        {
                                            //設定如果穿樑套管>該樑斷面的1/3，則無法放置穿樑套管
                                            message = "樑的斷面尺寸過小，無法放置穿樑套管，請將亮顯的管線改走樑下";
                                            elements.Insert(pickPipe);
                                            return Result.Failed;
                                        }
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
                                    }
                                    //對標註設定是鋼構開孔還是RC開孔

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
                    //MessageBox.Show($"共交集{intersectCount}處，總交集長度為{intersectLength * 30.48}");
                    //MessageBox.Show("穿樑套管放置完成!!");
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
                string RC_CastName = "穿樑套管共用參數_通用模型";
                Family RC_CastType = null;
                ElementFilter RC_CastCategoryFilter = new ElementCategoryFilter(BuiltInCategory.OST_PipeAccessory);
                ElementFilter RC_CastFamilyFilter = new ElementClassFilter(typeof(Family));

                LogicalAndFilter andFilter = new LogicalAndFilter(RC_CastCategoryFilter, RC_CastFamilyFilter);
                FilteredElementCollector RC_CastFamily = new FilteredElementCollector(doc);
                RC_CastFamily = RC_CastFamily.WherePasses(RC_CastFamilyFilter);//這地方有點怪，無法使用andFilter
                bool symbolFound = false;

                foreach (Family family in RC_CastFamily)
                {
                    if (family.Name == RC_CastName)
                    {
                        symbolFound = true;
                        RC_CastType = family;
                        break;
                    }
                }
                //如果沒有找到，則自己加載
                if (!symbolFound)
                {
                    //string filePath = @"D:\大陸工程\Dropbox (CHC Group)\工作人生\組內專案\04.元件製作\穿樑套管\穿樑套管測試_雙層樓模板.rfa";
                    //string filePath = @"D:\Dropbox (CHC Group)\工作人生\組內專案\04.元件製作\穿樑套管\穿樑套管測試_雙層樓模板.rfa";
                    string filePath = @"D:\Dropbox (CHC Group)\工作人生\組內專案\04.元件製作\穿樑套管\穿樑套管共用參數_通用模型.rfa";
                    Family family;
                    bool loadSuccess = doc.LoadFamily(filePath, out family);
                    if (loadSuccess)
                    {
                        RC_CastType = family;
                    }
                }

                return RC_CastType;
            }

            //根據不同的管徑，選擇不同的穿樑套管大小
            public FamilySymbol findRC_CastSymbol(Document doc, Family CastFamily, Element element)
            {
                FamilySymbol targetFamilySymbol = null; //用來找目標familySymbol
                //如果確定找到family後，針對不同得管選取不同的穿樑套管大小，以大兩吋為規則，如果有坡度則大三吋
                if (CastFamily != null)
                {
                    foreach (ElementId castId in CastFamily.GetFamilySymbolIds())
                    {
                        FamilySymbol tempSymbol = doc.GetElement(castId) as FamilySymbol;
                        //if (element.LookupParameter("直徑").AsValueString() == "50 mm")
                        if (element.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).AsValueString() == "40 mm")
                        {
                            if (tempSymbol.Name == "65mm")
                            {
                                targetFamilySymbol = tempSymbol;
                            }
                        }
                        else if (element.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).AsValueString() == "50 mm")
                        {
                            if (tempSymbol.Name == "80mm")
                            {
                                targetFamilySymbol = tempSymbol;
                            }
                        }
                        else if (element.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).AsValueString() == "65 mm")
                        {
                            if (tempSymbol.Name == "90mm")
                            {
                                targetFamilySymbol = tempSymbol;
                            }
                        }
                        else if (element.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).AsValueString() == "80 mm")
                        {
                            if (tempSymbol.Name == "100mm")
                            {
                                targetFamilySymbol = tempSymbol;
                            }
                        }
                        else if (element.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).AsValueString() == "100 mm")
                        {
                            if (tempSymbol.Name == "125mm")
                            {
                                targetFamilySymbol = tempSymbol;
                            }
                        }
                        else if (element.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).AsValueString() == "125 mm")
                        {
                            if (tempSymbol.Name == "150mm")
                            {
                                targetFamilySymbol = tempSymbol;
                            }
                        }
                        else if (element.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).AsValueString() == "150 mm")
                        {
                            if (tempSymbol.Name == "200mm")
                            {
                                targetFamilySymbol = tempSymbol;
                            }
                        }
                        else if (element.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).AsValueString() == "200 mm")
                        {
                            if (tempSymbol.Name == "250mm")
                            {
                                targetFamilySymbol = tempSymbol;
                            }
                        }
                        else if (element.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).AsValueString() == "250 mm")
                        {
                            if (tempSymbol.Name == "300mm")
                            {
                                targetFamilySymbol = tempSymbol;
                            }
                        }
                        else
                        {
                            if (tempSymbol.Name == "65mm")
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

        //製作一個方法，找到選中的樑中的其他套管
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

        public List<Element> otherCast_elem(Document doc, Element elem)
        {
            List<Element> castIntersected = new List<Element>();
            FilteredElementCollector otherCastCollector = new FilteredElementCollector(doc);
            ElementFilter RC_CastFilter = new ElementCategoryFilter(BuiltInCategory.OST_PipeAccessory);
            ElementFilter Cast_InstFilter = new ElementClassFilter(typeof(FamilyInstance));
            LogicalAndFilter andFilter = new LogicalAndFilter(RC_CastFilter, Cast_InstFilter);
            Outline beamOtLn = new Outline(elem.get_BoundingBox(null).Min ,elem.get_BoundingBox(null).Max);
            BoundingBoxIntersectsFilter beamBoundingBoxFilter = new BoundingBoxIntersectsFilter(beamOtLn);


            otherCastCollector.WherePasses(andFilter).WhereElementIsNotElementType();
            otherCastCollector.WherePasses(beamBoundingBoxFilter).ToElements();
            //otherCastCollector.WherePasses(new ElementIntersectsSolidFilter(solid));
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
                if (element.Category.Name == "管")
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
    }
}

