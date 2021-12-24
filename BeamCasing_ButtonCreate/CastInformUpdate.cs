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
    class CastInformUpdate : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            DisplayUnitType unitType = DisplayUnitType.DUT_MILLIMETERS;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            // the code that you want to measure comes here
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


                //製作一個容器放所有被實做出來的套管元件，先篩出所有doc中的familyInstance
                //再把指定名字的實體元素加入容器中
                List<FamilyInstance> castInstance = new List<FamilyInstance>();

                FilteredElementCollector coll = new FilteredElementCollector(doc);
                ElementCategoryFilter castCate_Filter = new ElementCategoryFilter(BuiltInCategory.OST_PipeAccessory);
                ElementClassFilter castInst_Filter = new ElementClassFilter(typeof(Instance));
                LogicalAndFilter andFilter = new LogicalAndFilter(castCate_Filter, castInst_Filter);
                coll.WherePasses(andFilter).WhereElementIsNotElementType().ToElements(); //找出模型中實做的穿樑套管元件
                if (coll != null)
                {
                    foreach (FamilyInstance e in coll)
                    {
                        if (e.Symbol.FamilyName == "穿樑套管共用參數_通用模型")
                        {
                            castInstance.Add(e);
                        }
                    }
                }
                int updateCastNum = 0;
                List<Element> intersectInst = new List<Element>();
                List<ElementId> updateCastIDs = new List<ElementId>();
                

                //先找出doc中所有的外參樑
                foreach (Document d in app.Documents)
                {
                    if (d.IsLinked)
                    {
                        FilteredElementCollector linkedBeams = new FilteredElementCollector(d).OfClass(typeof(Instance)).OfCategory(BuiltInCategory.OST_StructuralFraming);

                        //MessageBox.Show($"連結模型中有{linkedBeams.Count()}個樑實例");
                        using (Transaction trans = new Transaction(doc))
                        {
                            trans.Start("更新穿樑套管資訊");
                            foreach (Element e in linkedBeams)
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
                                        //針對不同的，加入他們的IDll
                                        SolidCurveIntersectionOptions options = new SolidCurveIntersectionOptions();
                                        //MessageBox.Show($"我是編號{e.LookupParameter("編號").AsString()}的樑，在我體內有{castInThisBeam.Count()}隻穿樑套管");
                                        foreach (Element inst in castInstance)
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

                                            //目前用curve去切樑實體的方法太不穩定

                                            if (intersectCount > 0)
                                            {
                                                //針對有交集的實體去做計算
                                                intersectInst.Add(inst);
                                                //將樑編號寫入元件中

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
                                                if (TTOP_update != TTOP_orgin)
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
                                            }
                                        }

                                        //蒐集特例
                                    }
                                }
                            }
                            trans.Commit();
                        }
                    }
                }
                //MessageBox.Show($"共有{intersectInst.Count()}個套管與樑有交集");
                if (updateCastNum > 0)
                {
                    string output = $"更新的穿樑套管有{updateCastNum}個，ID如下：\n";
                    //string output = $"更新的穿樑套管有{updateCastIDs.Count()}個，ID如下：\n";
                    foreach (ElementId id in updateCastIDs)
                    {
                        output += $"{id};";
                    }
                    MessageBox.Show(output, "CEC-MEP", MessageBoxButtons.OKCancel);
                    //TaskDialog.Show("Revit", output);
                    //MessageBox.Show(test);
                }
                else if (updateCastNum == 0)
                {
                    MessageBox.Show("所有套管資訊都已更新完畢!!", "CEC-MEP", MessageBoxButtons.OKCancel);
                }
                MessageBox.Show($"這個模型中有{castInstance.Count()}個實做的穿樑套管");
            }
            catch
            {
                message = "執行失敗!";
                return Result.Failed;
            }
            watch.Stop();
            var elapsedMs = watch.ElapsedMilliseconds;
            MessageBox.Show($"共花費{elapsedMs / 100}秒");
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
    }
}
