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
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                UIApplication uiapp = commandData.Application;
                Autodesk.Revit.ApplicationServices.Application app = uiapp.Application;
                UIDocument uidoc = commandData.Application.ActiveUIDocument;
                Document doc = uidoc.Document;

                //製作兩個List裝取所有的外參樑element與外參樑solid
                List<Element> linkedBeam_elems = new List<Element>();
                List<Solid> linkedBeam_solids = new List<Solid>();
                foreach (Document d in app.Documents)
                {
                    if (d.IsLinked)
                    {
                        FilteredElementCollector linkedBeams = new FilteredElementCollector(d).OfClass(typeof(Instance)).OfCategory(BuiltInCategory.OST_StructuralFraming);
                        MessageBox.Show($"連結模型中有{linkedBeams.Count()}個樑實例");
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
                                    List<Element> castInThisBeam = otherCast_elem(doc, e);
                                    if (castInThisBeam.Count() > 0)
                                    {
                                        linkedBeam_elems.Add(e);
                                        MessageBox.Show($"我是編號{e.LookupParameter("編號").AsString()}的樑，在我體內有{castInThisBeam.Count()}隻穿樑套管");
                                    }
                                }
                                else
                                {
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                MessageBox.Show("執行失敗喔");
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
            LogicalAndFilter andFilter2 = new LogicalAndFilter(andFilter, beamBoundingBoxFilter);

            otherCastCollector.WherePasses(andFilter2).WhereElementIsNotElementType().ToElements() ;
            //otherCastCollector.WherePasses(beamBoundingBoxFilter).ToElements();
            //otherCastCollector.WherePasses(new ElementIntersectsSolidFilter(solid));
            foreach (Element e in otherCastCollector)
            {
                FamilyInstance inst = e as FamilyInstance;
                if (inst != null)
                {
                    if (inst.Symbol.FamilyName == "穿樑套管共用參數_通用模型")
                    {
                        castIntersected.Add(e);
                    }
                }
            }
            return castIntersected;
        }
    }
}
