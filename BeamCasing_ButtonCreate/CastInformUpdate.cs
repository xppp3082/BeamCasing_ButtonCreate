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
                        if (e.Symbol.FamilyName == "穿樑套管共用參數_雙層樓模板")
                        {
                            castInstance.Add(e);
                        }
                    }
                }
                string test="";
                int updateCastNum = 0;

                List<ElementId> updateCastIDs = new List<ElementId>();
                //先找出doc中所有的外參樑
                foreach (Document d in app.Documents)
                {
                    if (d.IsLinked)
                    {
                        FilteredElementCollector linkedBeams = new FilteredElementCollector(d).OfClass(typeof(Instance)).OfCategory(BuiltInCategory.OST_StructuralFraming);
                        using (Transaction trans = new Transaction(doc))
                        {
                            //MessageBox.Show($"連結模型中有{linkedBeams.Count()}個樑實例");
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
                                        //針對不同的，加入他們的ID
                                        SolidCurveIntersectionOptions options = new SolidCurveIntersectionOptions();
                                        foreach (FamilyInstance inst in castInstance)
                                        {
                                            LocationPoint instLocate = inst.Location as LocationPoint; //原本locationPoint的Z值為0，要針對這個去做調整
                                            ElementId referLevelId = inst.LookupParameter("頂部樓層").AsElementId();
                                            Level referLevel = doc.GetElement(referLevelId) as Level;
                                            double instTopOffset = inst.LookupParameter("頂部偏移").AsDouble();
                                            double Z_adjust = referLevel.Elevation + instTopOffset;

                                            XYZ instPt = instLocate.Point;
                                            XYZ inst_Up = new XYZ(instPt.X, instPt.Y, Z_adjust + 50);
                                            XYZ inst_Dn = new XYZ(instPt.X, instPt.Y, Z_adjust - 50);
                                            Curve instVerticalCrv = Line.CreateBound(inst_Dn, inst_Up);

                                            SolidCurveIntersection intersection = solid.IntersectWithCurve(instVerticalCrv, options);
                                            int intersectCount = intersection.SegmentCount;
                                            //if (intersectCount > 0)
                                            if (intersectCount == 1)
                                            {
                                                //針對有交集的實體去做計算
                                                //目前還有問題，應該是因為TOP跟BOP的計算有錯
                                                LocationPoint cast_Locate = inst.Location as LocationPoint;
                                                XYZ LocationPt = cast_Locate.Point;
                                                XYZ cast_Max = inst.get_BoundingBox(null).Max;
                                                XYZ cast_Min = inst.get_BoundingBox(null).Min;
                                                Curve castIntersect_Crv = intersection.GetCurveSegment(0);
                                                XYZ intersect_DN = castIntersect_Crv.GetEndPoint(0);
                                                XYZ intersect_UP = castIntersect_Crv.GetEndPoint(1);
                                                //XYZ thisBeam_Max=e.get_BoundingBox(null).;
                                                //XYZ thisBeam_Min;


                                                double TOP_update = intersect_UP.Z - cast_Max.Z;
                                                double BOP_update = cast_Min.Z - intersect_DN.Z;
                                                //MessageBox.Show($"交集的上緣Z值為:{intersect_UP.Z}，下緣Z值為:{intersect_DN.Z}，這個穿樑套管的Z值為{LocationPt.Z}");
                                                double TOP_orgin = inst.LookupParameter("TOP").AsDouble();
                                                double BOP_orgin = inst.LookupParameter("BOP").AsDouble();
                                                //test += $"TOP_update:{TOP_update}，TOP_orgin:{TOP_orgin}\n";
                                                //test += $"BOP_update:{BOP_update}，BOP_orgin:{BOP_orgin}\n";
                                                //test += $"cast_Max:{cast_Max.Z*30.48}，cast_Min:{cast_Min.Z*30.48}\n";
                                                //updateCastNum += 1;
                                                if (TOP_update != TOP_orgin)
                                                {
                                                    inst.LookupParameter("TOP").Set(TOP_update);
                                                    inst.LookupParameter("BOP").Set(BOP_update);
                                                    updateCastNum += 1;
                                                    Element updateElem = inst as Element;
                                                    updateCastIDs.Add(updateElem.Id);
                                                }
                                                else if (TOP_update == TOP_orgin)
                                                {
                                                    break;
                                                }
                                            }
;
                                        }
                                    }
                                }
                            }
                            trans.Commit();
                        }
                    }
                }
                string output = $"更新的穿樑套管有{updateCastNum}個，ID如下：\n";
                //string output = $"更新的穿樑套管有{updateCastIDs.Count()}個，ID如下：\n";
                foreach (ElementId id in updateCastIDs)
                {
                    output += $"{id};";
                }
                //TaskDialog.Show("Revit", output);
                MessageBox.Show(output);
                MessageBox.Show(test);


                MessageBox.Show($"這個模型中有{castInstance.Count()}個實做的穿樑套管");
                return Result.Succeeded;
            }
            catch
            {
                return Result.Failed;
            }
        }
    }
}
