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
using System.Text;
#endregion

namespace BeamCasing_ButtonCreate
{
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    class CastInformUpdateV2 : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            DisplayUnitType unitType = DisplayUnitType.DUT_MILLIMETERS;
            try
            {
                //程式要做四件事如下:
                //1.抓到所有的外參樑
                //2.抓到所有被實做出來的套管
                //3.讓套管和外參樑做交集，比較回傳的參數值是否一樣，不一樣則調整
                //4.並把這些套管亮顯或ID寫下來
                UIApplication uiapp = commandData.Application;
                Autodesk.Revit.ApplicationServices.Application app = uiapp.Application;
                UIDocument uidoc = commandData.Application.ActiveUIDocument;
                Document doc = uidoc.Document;

                string CastName = "穿樑套管共用參數_通用模型";



                //製作一個容器放所有被實做出來的套管元件，先篩出所有doc中的familyInstance
                //再把指定名字的實體元素加入容器中
                List<FamilyInstance> castInstances = new List<FamilyInstance>();
                FilteredElementCollector coll = new FilteredElementCollector(doc);
                ElementCategoryFilter castCate_Filter = new ElementCategoryFilter(BuiltInCategory.OST_PipeAccessory);
                ElementClassFilter castInst_Filter = new ElementClassFilter(typeof(Instance));
                LogicalAndFilter andFilter = new LogicalAndFilter(castCate_Filter, castInst_Filter);
                coll.WherePasses(andFilter).WhereElementIsNotElementType().ToElements(); //找出模型中實做的穿樑套管元件
                if (coll != null)
                {
                    foreach (FamilyInstance e in coll)
                    {
                        if (e.Symbol.FamilyName == CastName)
                        {
                            castInstances.Add(e);
                        }
                    }
                }
                //建置一個List來裝外參中所有的RC樑
                List<Element> RC_Beams = new List<Element>();
                List<Element> ST_Beams = new List<Element>();
                ICollection<ElementId> RC_BeamsID = new List<ElementId>();
                ICollection<ElementId> ST_BeamsID = new List<ElementId>();

                //建置蒐集特例的List
                int updateCastNum = 0;
                List<Element> intersectInst = new List<Element>();
                List<ElementId> updateCastIDs = new List<ElementId>();
                List<Element> Cast_tooClose = new List<Element>(); //存放離樑頂或樑底太近的套管
                List<Element> Cast_tooBig = new List<Element>(); //存放太大的套管
                List<Element> Cast_Conflict = new List<Element>(); //存放彼此太過靠近的套管

                //先找出doc中所有的外參樑
                foreach (Document d in app.Documents)
                {
                    if (d.IsLinked)
                    {
                        FilteredElementCollector linkedBeams = new FilteredElementCollector(d).OfClass(typeof(Instance)).OfCategory(BuiltInCategory.OST_StructuralFraming);
                        foreach (Element e in linkedBeams)
                        {
                            FamilyInstance beamInstance = e as FamilyInstance;
                            if (beamInstance.StructuralMaterialType.ToString() == "Concrete")
                            {
                                RC_Beams.Add(e);
                                RC_BeamsID.Add(e.Id);
                            }
                            else if (beamInstance.StructuralMaterialType.ToString() == "Steel")
                            {
                                ST_Beams.Add(e);
                                ST_BeamsID.Add(e.Id);
                            }
                        }
                    }
                }
                MessageBox.Show(ST_BeamsID.First().ToString());
                MessageBox.Show($"連結模型中有{RC_Beams.Count()}個樑實例");

                //FilteredElementCollector SRCcollector = new FilteredElementCollector(ST_Beams.First().Document, ST_BeamsID);


                using (Transaction trans = new Transaction(doc))
                {
                    trans.Start("更新穿樑套管資訊");
                    foreach (Element e in RC_Beams)
                    {
                        Options geoOptions = new Options();
                        geoOptions.ComputeReferences = true;
                        geoOptions.DetailLevel = ViewDetailLevel.Fine;
                        //利用foreach迴圈找出geoElements 中的solid部分，進行交集
                        GeometryElement geoElement = e.get_Geometry(geoOptions);
                        foreach (GeometryObject obj in geoElement)
                        {
                            Solid solid = obj as Solid;
                            if (null != solid)
                            {
                                //用每個被實做出來的套管針對這隻樑進行檢查
                                //針對有交集的套管，計算TOP和BOP是否相同
                                //針對不同的，加入他們的ID
                                SolidCurveIntersectionOptions options = new SolidCurveIntersectionOptions();
                                //MessageBox.Show($"我是編號{e.LookupParameter("編號").AsString()}的樑，在我體內有{castInThisBeam.Count()}隻穿樑套管");
                                foreach (Element inst in castInstances)
                                {
                                    LocationPoint instLocate = inst.Location as LocationPoint;
                                    double inst_CenterZ = (inst.get_BoundingBox(null).Max.Z + inst.get_BoundingBox(null).Min.Z) / 2;
                                    XYZ instPt = instLocate.Point;
                                    double normal_BeamHeight = UnitUtils.ConvertToInternalUnits(1000, unitType);
                                    XYZ inst_Up = new XYZ(instPt.X, instPt.Y, instPt.Z + normal_BeamHeight);
                                    XYZ inst_Dn = new XYZ(instPt.X, instPt.Y, instPt.Z - normal_BeamHeight);
                                    Curve instVerticalCrv = Line.CreateBound(inst_Dn, inst_Up);

                                    //這邊用solid是因為怕有斜樑需要開口的問題，但斜樑的結構應力應該已經蠻集中的，不可以再開口
                                    SolidCurveIntersection intersection = solid.IntersectWithCurve(instVerticalCrv, options);
                                    int intersectCount = intersection.SegmentCount;


                                    //針對有切割到的實體去做計算
                                    if (intersectCount > 0)
                                    {
                                        //針對有交集的實體去做計算
                                        intersectInst.Add(inst);

                                        //計算TOP、BOP等六個參數
                                        LocationPoint cast_Locate = inst.Location as LocationPoint;
                                        XYZ LocationPt = cast_Locate.Point;
                                        XYZ cast_Max = inst.get_BoundingBox(null).Max;
                                        XYZ cast_Min = inst.get_BoundingBox(null).Min;
                                        Curve castIntersect_Crv = intersection.GetCurveSegment(0);
                                        XYZ intersect_DN = castIntersect_Crv.GetEndPoint(0);
                                        XYZ intersect_UP = castIntersect_Crv.GetEndPoint(1);
                                        double castCenter_Z = (cast_Max.Z + cast_Min.Z) / 2;

                                        double TTOP_update = intersect_UP.Z - cast_Max.Z;
                                        double BTOP_update = cast_Max.Z - intersect_DN.Z;
                                        double TCOP_update = intersect_UP.Z - castCenter_Z;
                                        double BCOP_update = castCenter_Z - intersect_DN.Z;
                                        double TBOP_update = intersect_UP.Z - cast_Min.Z;
                                        double BBOP_update = cast_Min.Z - intersect_DN.Z;
                                        //MessageBox.Show($"交集的上緣Z值為:{intersect_UP.Z}，下緣Z值為:{intersect_DN.Z}，這個穿樑套管的Z值為{LocationPt.Z}");
                                        double TTOP_orgin = inst.LookupParameter("TTOP").AsDouble();
                                        double BBOP_orgin = inst.LookupParameter("BBOP").AsDouble();
                                        double beamHeight = intersect_UP.Z - intersect_DN.Z;
                                        double castHeight = cast_Max.Z - cast_Min.Z;

                                        double TTOP_Check = Math.Round(UnitUtils.ConvertFromInternalUnits(TTOP_update, unitType), 1);
                                        double TTOP_orginCheck = Math.Round((UnitUtils.ConvertFromInternalUnits(TTOP_orgin, unitType)), 1);

                                        if (TTOP_Check != TTOP_orginCheck)
                                        {
                                            inst.LookupParameter("TTOP").Set(TTOP_update);
                                            inst.LookupParameter("BTOP").Set(BTOP_update);
                                            inst.LookupParameter("TCOP").Set(TCOP_update);
                                            inst.LookupParameter("BCOP").Set(BCOP_update);
                                            inst.LookupParameter("TBOP").Set(TBOP_update);
                                            inst.LookupParameter("BBOP").Set(BBOP_update);
                                            updateCastNum += 1;
                                            Element updateElem = inst as Element;
                                            updateCastIDs.Add(updateElem.Id);
                                        }

                                        //寫入樑編號與量尺寸
                                        string beamName = e.LookupParameter("編號").AsString();
                                        string beamSIze = e.LookupParameter("類型").AsValueString();//抓取類型
                                        if (beamName != null)
                                        {
                                            inst.LookupParameter("貫穿樑編號").Set(beamName);
                                        }
                                        else
                                        {
                                            inst.LookupParameter("貫穿樑編號").Set("無編號");
                                        }
                                        if (beamSIze != null)
                                        {
                                            inst.LookupParameter("貫穿樑尺寸").Set(beamSIze);
                                        }
                                        else
                                        {
                                            inst.LookupParameter("貫穿樑尺寸").Set("無尺寸");
                                        }

                                        //太過靠近樑底的套管
                                        double alertValue = beamHeight / 4; //設定樑底與樑頂1/4的距離警告
                                                                            //if (inst.LookupParameter("TTOP").AsDouble() < alertValue || inst.LookupParameter("BBOP").AsDouble() < alertValue)
                                        if (TTOP_update < alertValue || BBOP_update < alertValue)
                                        {
                                            Cast_tooClose.Add(inst);
                                        }
                                        //太大的套管
                                        double alertMaxSize = beamHeight / 3;//設定1/3的尺寸警告
                                        if (castHeight > alertMaxSize)
                                        {
                                            Cast_tooBig.Add(inst);
                                        }
                                        ////判斷是鋼構開孔還是RC開孔，先判斷這隻RC樑中有幾隻鋼構樑
                                        //IList<Element> checkBeams = STBeamsInside(doc,ST_BeamsID,e);
                                        //if (checkBeams.Count() >= 1)
                                        //{
                                        //    inst.get_Parameter(BuiltInParameter.ALL_MODEL_MARK).Set("鋼構開孔");
                                        //}
                                        //else
                                        //{
                                        //    inst.get_Parameter(BuiltInParameter.ALL_MODEL_MARK).Set("RC開口");
                                        //}
                                    }
                                }
                            }
                        }
                    }
                    trans.Commit();
                }

                ////MessageBox.Show($"共有{intersectInst.Count()}個套管與樑有交集");
                //if (updateCastNum > 0)
                //{
                //    string output = $"更新的穿樑套管有{updateCastNum}個，ID如下：\n";
                //    //string output = $"更新的穿樑套管有{updateCastIDs.Count()}個，ID如下：\n";
                //    foreach (ElementId id in updateCastIDs)
                //    {
                //        output += $"{id};";
                //    }
                //    if (Cast_tooClose.Count() > 0 || Cast_tooBig.Count() > 0 || Cast_Conflict.Count() > 0)
                //    {
                //        output += $"\n有幾個套管有問題，歸類如下：";
                //        output += $"\n離樑底或樑底過近的套管有{Cast_tooClose.Count()}個，ID如下—\n";
                //        foreach (Element e in Cast_tooClose)
                //        {
                //            output += $"{e.Id};";
                //        }
                //        output += $"\n尺寸過大的套管有{Cast_tooBig.Count()}個，ID如下—\n";
                //        foreach (Element e in Cast_tooBig)
                //        {
                //            output += $"{e.Id};";
                //        }
                //        output += $"\n與其他套管過近的的套管有{Cast_Conflict.Count()}個，ID如下—\n";
                //        foreach (Element e in Cast_Conflict)
                //        {
                //            output += $"{e.Id};";
                //        }
                //    }
                //    MessageBox.Show(output, "CEC-MEP", MessageBoxButtons.OKCancel);
                //}
                ////如果有不符合穿樑原則的：
                //else if (Cast_tooClose.Count() > 0 || Cast_tooBig.Count() > 0 || Cast_Conflict.Count() > 0)
                //{
                //    string output2 = $"有幾個套管有問題，歸類如下：";
                //    output2 += $"\n離樑底或樑底過近的套管有{Cast_tooClose.Count()}個，ID如下—\n";
                //    foreach (Element e in Cast_tooClose)
                //    {
                //        output2 += $"{e.Id};";
                //    }
                //    output2 += $"\n尺寸過大的套管有{Cast_tooBig.Count()}個，ID如下—\n";
                //    foreach (Element e in Cast_tooBig)
                //    {
                //        output2 += $"{e.Id};";
                //    }
                //    output2 += $"\n與其他套管過近的的套管有{Cast_Conflict.Count()}個，ID如下—\n";
                //    foreach (Element e in Cast_Conflict)
                //    {
                //        output2 += $"{e.Id};";
                //    }
                //    MessageBox.Show(output2, "CEC-MEP", MessageBoxButtons.OKCancel);
                //}
                //else if (updateCastNum == 0)
                //{
                //    MessageBox.Show("所有套管資訊都已更新完畢!!", "CEC-MEP", MessageBoxButtons.OKCancel);
                //}
                //MessageBox.Show($"這個模型中有{castInstance.Count()}個實做的穿樑套管");
            }
            catch
            {
                message = "執行失敗!";
                return Result.Failed;
            }
            return Result.Succeeded;
        }
        public List<Element> otherCast_elem(Document doc, Element elem)
        {
            //這邊要注意，用solid和element取boundingbox的做法是有差別的，他們會出現在不同的坐標點位
            List<Element> castIntersected = new List<Element>();
            FilteredElementCollector otherCastCollector = new FilteredElementCollector(doc);
            ElementFilter RC_CastFilter = new ElementCategoryFilter(BuiltInCategory.OST_PipeAccessory);
            ElementFilter Cast_InstFilter = new ElementClassFilter(typeof(FamilyInstance));
            LogicalAndFilter andFilter = new LogicalAndFilter(RC_CastFilter, Cast_InstFilter);
            Outline beamOtLn = new Outline(elem.get_BoundingBox(null).Min, elem.get_BoundingBox(null).Max);
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

        //製做一個方法，取的樑的solid

        public IList<Solid> GetTargetSolids(Element element)
        {
            List<Solid> solids = new List<Solid>();
            Options options = new Options();
            //預設為不包含不可見元件，因此改成true
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
            //var newSolid = SolidUtils.CreateTransformed(solidResult, tr);
            //return newSolid;
            return solidResult;
        }

        //針對所有的套管(多個)去對鋼樑(一個)做碰撞
        private IList<Element> STBeamsInside(Document linkDoc, ICollection<ElementId> ST_BeamsID, Element RC_Beam)
        {
            FilteredElementCollector collector = new FilteredElementCollector(linkDoc, ST_BeamsID);
            Outline RC_Bounding = new Outline(RC_Beam.get_BoundingBox(null).Max, RC_Beam.get_BoundingBox(null).Min);
            BoundingBoxIntersectsFilter RC_boundFilter = new BoundingBoxIntersectsFilter(RC_Bounding);
            Solid RC_Solid = singleSolidFromElement(RC_Beam);
            ElementIntersectsSolidFilter RC_solidFIlter = new ElementIntersectsSolidFilter(RC_Solid);
            IList<Element>SRCcollector =  collector.WherePasses(RC_boundFilter).WherePasses(RC_solidFIlter).ToElements();

            return SRCcollector;
        }

        //針對所有的套管(多個)去對鋼樑(一個)做碰撞
    }
}
