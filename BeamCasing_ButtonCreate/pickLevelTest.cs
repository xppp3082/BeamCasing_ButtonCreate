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
    class pickLevelTest : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIDocument uidoc = commandData.Application.ActiveUIDocument;
                ISelectionFilter pipefilter = new PipeSelectionFilter();
                Document doc = uidoc.Document;

                //點選要放置穿樑套管的管段
                Reference pickElements_refer = uidoc.Selection.PickObject(ObjectType.Element, pipefilter, $"請選擇欲放置穿樑套管的管段");
                Element pickPipe = doc.GetElement(pickElements_refer.ElementId);

                FilteredElementCollector levelCollector = new FilteredElementCollector(doc);
                ElementFilter level_Filter = new ElementCategoryFilter(BuiltInCategory.OST_Levels);
                levelCollector.WherePasses(level_Filter).WhereElementIsNotElementType().ToElements();

                string output = "";
                List<string> levelNames = new List<string>();

                List<Element> level_List = levelCollector.OrderBy(x => sortLevelbyHeight(x)).ToList();


                for (int i = 0; i < levelCollector.Count(); i++)
                {
                    //Level le = levelCollector.ToList()[i] as Level;
                    Level le = level_List[i] as Level;
                    levelNames.Add(le.Name);
                    output+= $"我是集合中的第{i}個樓層，我的名字是{le.Name}，我的高度是{le.LookupParameter("立面").AsValueString()}\n";
                }
                //利用index反查樓層的位置，就可以用這個方式反推他的上一個樓層
                MEPCurve pipeCrv = pickPipe as MEPCurve;
                Level pipeLevel = pipeCrv.ReferenceLevel;
                int index_pipe = levelNames.IndexOf(pipeLevel.Name);
                output += $"我是選中的管的樓層，我的名字是{pipeLevel.Name}，我是集合中的第{index_pipe}個元素";

                MessageBox.Show(output);
            }
            catch
            {
                return Result.Failed;
            }
            return Result.Succeeded;
        }

        public double sortLevelbyHeight(Element element)
        {
            Level tempLevel = element as Level;
            double levelHeight = element.LookupParameter("立面").AsDouble();
            return levelHeight;
        }

    }
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

}
