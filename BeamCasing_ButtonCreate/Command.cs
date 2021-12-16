﻿#region Namespaces
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
    public class BeamCasing : IExternalCommand
    {
        //點選管創建穿樑套管
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            //try
            //{
            UIApplication uiapp = commandData.Application;
            Autodesk.Revit.ApplicationServices.Application app = uiapp.Application;

            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            ISelectionFilter pipefilter = new PipeSelectionFilter();
            //ICollection<ElementId> ids = uidoc.Selection.GetElementIds();
            Document doc = uidoc.Document;

            //點選要放置的多管吊架
            Reference pickElements_refer = uidoc.Selection.PickObject(ObjectType.Element, pipefilter, $"請選擇欲放置穿樑套管的管段");
            Element pickPipe = doc.GetElement(pickElements_refer.ElementId);

            Family RC_Cast;


            using (Transaction tx = new Transaction(doc))
            {
                tx.Start("載入檔案測試");

                RC_Cast = new BeamCast().BeamCastSymbol(doc);
                MessageBox.Show($"找到的元件名稱為:{RC_Cast.Name}");
                tx.Commit();
            }

            using (Transaction tx = new Transaction(doc))
            {
                tx.Start("尋找元件");
                Element CastSymbol = new BeamCast().findRC_CastSymbol(doc, RC_Cast, pickPipe);
                MessageBox.Show($"找到的元件名稱為:{CastSymbol.Name}");
                //MessageBox.Show("執行成功!");
                tx.Commit();
            }

            //尋找連結模型中的元素_方法1
            ElementFilter instanceFilter = new ElementClassFilter(typeof(Instance));
            ElementFilter linkFilter = new ElementCategoryFilter(BuiltInCategory.OST_RvtLinks);
            ElementFilter structuralFilter = new ElementCategoryFilter(BuiltInCategory.OST_StructuralFraming);
            LogicalAndFilter andFilter2 = new LogicalAndFilter(structuralFilter, linkFilter);
            LogicalAndFilter andFilter = new LogicalAndFilter(instanceFilter, linkFilter);
            FilteredElementCollector linked_BeamCollector = new FilteredElementCollector(doc);
            linked_BeamCollector = linked_BeamCollector.WherePasses(andFilter);
            MessageBox.Show($"連結模型中總共有{linked_BeamCollector.Count()}個族群實例");

            //尋找連結模型中的元素_方法2
            IList<FamilyInstance> CastList = new List<FamilyInstance>(); //創造一個裝每次被創造出來的familyinstance的容器，用來以bounding box計算bop&top
            FamilyInstance instance = null;
            int intersectCount = 0;
            double intersectLength = 0;
            foreach (Document d in app.Documents)
            {
                if (d.IsLinked)
                {
                    Debug.Print(string.Format("Link docment '{0}':", d.Title));

                    FilteredElementCollector beams = new FilteredElementCollector(d).OfClass(typeof(Instance)).OfCategory(BuiltInCategory.OST_StructuralFraming);
                    MessageBox.Show($"連結模型中總共有{beams.Count()}個樑實例");

                    //抓取樑實例，並與線取交集
                    //先測試，如果管和樑交集，產生一條model line
                    if (beams.Count() > 0)
                    {
                        foreach (Element e in beams)
                        {
                            Options geomOptions = new Options();
                            geomOptions.ComputeReferences = true;
                            geomOptions.DetailLevel = ViewDetailLevel.Fine;
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
                                    for (int i = 0; i < intersectCount; i++)
                                    {
                                        intersectLength += intersection.GetCurveSegment(i).Length;
                                        Curve tempCurve = intersection.GetCurveSegment(i);
                                        XYZ tempCenter = tempCurve.Evaluate(0.5, true);
                                        FamilySymbol CastSymbol2 = new BeamCast().findRC_CastSymbol(doc, RC_Cast, pickPipe);
                                        MEPCurve mepCurve = pickPipe as MEPCurve;
                                        Level castLevel = mepCurve.ReferenceLevel;
                                        using (Transaction tx = new Transaction(doc))
                                        {
                                            tx.Start("創造穿樑套管");
                                            //創建穿樑套管
                                            instance = doc.Create.NewFamilyInstance(tempCenter, CastSymbol2, castLevel, StructuralType.NonStructural);

                                            //調整長度與高度
                                            instance.LookupParameter("長度").Set(intersection.GetCurveSegment(i).Length + 2 / 30.48); //套管前後加兩公分
                                            Level topLevel = doc.GetElement(instance.LookupParameter("頂部樓層").AsElementId()) as Level;
                                            Level baseLevel = doc.GetElement(instance.LookupParameter("基準樓層").AsElementId()) as Level;
                                            double floorHeigth = topLevel.Elevation - baseLevel.Elevation;
                                            double toMove2 = tempCenter.Z - topLevel.Elevation + pickPipe.LookupParameter("直徑").AsDouble();
                                            double toMove = pickPipe.LookupParameter("偏移").AsDouble() - floorHeigth;
                                            instance.LookupParameter("頂部偏移").Set(toMove2);

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
                                                instance.LookupParameter("系統別").Set("GA");
                                            }
                                            else
                                            {
                                                instance.LookupParameter("系統別").Set("未指定");
                                            }

                                            ////switch的版本
                                            //switch (pipeSystem)
                                            //{
                                            //    case "P 排水-汙水(SP)":
                                            //        instance.LookupParameter("系統別").Set("P");
                                            //        break;
                                            //    case "P 排水-廢水(WP)":
                                            //        instance.LookupParameter("系統別").Set("P");
                                            //        break;
                                            //}


                                            tx.Commit();
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            MessageBox.Show($"共交集{intersectCount}處，總交集長度為{intersectLength * 30.48}");
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
                string RC_CastName = "穿樑套管測試_雙層樓模板";
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
                    string filePath = @"D:\Dropbox (CHC Group)\工作人生\組內專案\04.元件製作\穿樑套管\穿樑套管測試_雙層樓模板.rfa";
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
                            if (tempSymbol.Name == "150mm")
                            {
                                targetFamilySymbol = tempSymbol;
                            }
                        }
                        else if (element.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).AsValueString() == "125 mm")
                        {
                            if (tempSymbol.Name == "200mm")
                            {
                                targetFamilySymbol = tempSymbol;
                            }
                        }
                        else if (element.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).AsValueString() == "150 mm")
                        {
                            if (tempSymbol.Name == "250mm")
                            {
                                targetFamilySymbol = tempSymbol;
                            }
                        }
                        else if (element.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).AsValueString() == "200 mm")
                        {
                            if (tempSymbol.Name == "300mm")
                            {
                                targetFamilySymbol = tempSymbol;
                            }
                        }
                        else if (element.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).AsValueString() == "250 mm")
                        {
                            if (tempSymbol.Name == "350mm")
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

            //找到管與連結模型的交集(最好是線資訊，可以計算長度)，並以線的中點為基礎，創造穿樑套管，MEP Curve和Beam Solid的交集
            //先建立一多個篩選器，將revit連結模型中的「樑」篩選出來

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
    }
}
