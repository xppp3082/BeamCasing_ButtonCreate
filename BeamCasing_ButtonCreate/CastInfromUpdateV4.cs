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
using System.IO;
#endregion

namespace BeamCasing_ButtonCreate
{
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    class CastInfromUpdateV4 : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();//引用stopwatch物件
            sw.Reset();//碼表歸零
            sw.Start();//碼表開始計時
            DisplayUnitType unitType = DisplayUnitType.DUT_MILLIMETERS;
            try
            {
                //步驟上進行如下，先挑出所有的RC樑，檢查RC樑中是否有ST樑，
                UIApplication uiapp = commandData.Application;
                Autodesk.Revit.ApplicationServices.Application app = uiapp.Application;
                UIDocument uidoc = commandData.Application.ActiveUIDocument;
                Document doc = uidoc.Document;
                List<string> usefulParaName = new List<string> { "BTOP", "BCOP", "BBOP", "TTOP", "TCOP", "TBOP",
                    "【原則檢討】上部檢討", "【原則檢討】下部檢討", "【原則檢討】尺寸檢討", "【原則檢討】是否穿樑", "【原則檢討】邊距檢討", "【原則檢討】樑端檢討",
                    "干涉管數量", "系統別", "貫穿樑尺寸", "貫穿樑材料", "貫穿樑編號" };
                List<double> settingPara = new List<double>() {
                    BeamCast_Settings.Default.cD1_Ratio,
                    BeamCast_Settings.Default.cP1_Ratio,
                    BeamCast_Settings.Default.cMax1_Ratio,
                    BeamCast_Settings.Default.rD1_Ratio,
                    BeamCast_Settings.Default.rP1_Ratio,
                    BeamCast_Settings.Default.rMax1_RatioD,
                    BeamCast_Settings.Default.rMax1_RatioW,
                    BeamCast_Settings.Default.cD2_Ratio,
                    BeamCast_Settings.Default.cP2_Ratio,
                    BeamCast_Settings.Default.cMax2_Ratio,
                    BeamCast_Settings.Default.rD2_Ratio,
                    BeamCast_Settings.Default.rP2_Ratio,
                    BeamCast_Settings.Default.rMax2_RatioD,
                    BeamCast_Settings.Default.rMax2_RatioW};

                List<double> settingPara2 = new List<double>()
                { BeamCast_Settings.Default.cP1_Min,BeamCast_Settings.Default.cMax1_Max,BeamCast_Settings.Default.rP1_Min,
                    BeamCast_Settings.Default.cP2_Min,BeamCast_Settings.Default.cMax2_Max,BeamCast_Settings.Default.rP2_Min
                };

                //檢查設定值是否正確
                foreach (double d in settingPara)
                {
                    if (d == 0)
                    {
                        message = "穿樑原則的設定值有誤(非選填數值不可為0)，請重新設定後再次執行";
                        return Result.Failed;
                    }
                }
                for (int i = 0; i < settingPara2.Count; i++)
                {
                    if (settingPara2[i] == null)
                    {
                        settingPara2[i] = 0;
                    }
                }

                //找到所有穿樑套管元件
                List<FamilyInstance> famList = findTargetElements(doc);


                //檢查參數
                foreach (FamilyInstance famInst in famList)
                {
                    foreach (string item in usefulParaName)
                    {
                        if (!checkPara(famInst, item))
                        {
                            MessageBox.Show($"執行失敗，請檢查{famInst.Symbol.FamilyName}元件中是否缺少{item}參數欄位");
                            return Result.Failed;
                        }
                    }
                }


                //更新穿樑套管參數
                using (Transaction trans = new Transaction(doc))
                {

                    Dictionary<ElementId, List<Element>> castDict = getCastBeamDict(doc);
                    trans.Start("更新關樑套管參數");
                    foreach (ElementId tempId in castDict.Keys)
                    {
                        modifyCastLen(doc.GetElement(tempId), castDict[tempId].First());
                        updateCastInst(doc.GetElement(tempId), castDict[tempId].First());
                        updateCastContent(doc, doc.GetElement(tempId));
                    }

                    //檢查所有的實做套管，如果不再Dictionary中，則表示其沒有穿樑，「【原則檢討】是否穿樑」應該設為不符合
                    List<FamilyInstance> famListUpdate = findTargetElements(doc);
                    foreach (FamilyInstance inst in famListUpdate)
                    {
                        if (!castDict.Keys.Contains(inst.Id))
                        {
                            inst.LookupParameter("【原則檢討】是否穿樑").Set("不符合");
                            inst.LookupParameter("貫穿樑尺寸").Set("無尺寸");
                            inst.LookupParameter("貫穿樑編號").Set("無編號");
                            inst.LookupParameter("干涉管數量").Set(0);
                            inst.LookupParameter("系統別").Set("未指定");
                        }
                    }
                    trans.Commit();
                }
            }
            catch
            {
                MessageBox.Show("執行失敗");
                return Result.Failed;
            }
            sw.Stop();//碼錶停止
            double sec = Math.Round(sw.Elapsed.TotalMilliseconds / 1000, 2);
            MessageBox.Show($"套管資訊更新完成，共花費 {sec} 秒");
            return Result.Succeeded;
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
        public List<RevitLinkInstance> getRCLinkedInstances(Document doc)
        {
            List<RevitLinkInstance> RClinkedInstances = new List<RevitLinkInstance>();
            Document RCfile = null;
            ElementCategoryFilter linkedFileFilter = new ElementCategoryFilter(BuiltInCategory.OST_RvtLinks);
            FilteredElementCollector linkedFileCollector = new FilteredElementCollector(doc).WherePasses(linkedFileFilter).WhereElementIsNotElementType();
            if (linkedFileCollector.Count() > 0)
            {
                foreach (RevitLinkInstance linkedInst in linkedFileCollector)
                {
                    Document linkDoc = linkedInst.GetLinkDocument();
                    if (linkDoc == null || !linkedInst.IsValidObject) continue;
                    FilteredElementCollector linkedBeams = new FilteredElementCollector(linkDoc).OfClass(typeof(Instance)).OfCategory(BuiltInCategory.OST_StructuralFraming);
                    foreach (Element e in linkedBeams)
                    {
                        FamilyInstance beamInstance = e as FamilyInstance;
                        if (beamInstance.StructuralMaterialType.ToString() == "Concrete" && !RClinkedInstances.Contains(linkedInst))
                        {
                            //RCfile = linkDoc;
                            RClinkedInstances.Add(linkedInst);
                        }
                    }
                }
            }
            else if (linkedFileCollector.Count() == 0)
            {
                MessageBox.Show("模型中沒有實做的外參檔案");
            }
            return RClinkedInstances;
        }
        public List<RevitLinkInstance> getSCLinkedInstances(Document doc)
        {
            List<RevitLinkInstance> SClinkedInstances = new List<RevitLinkInstance>();
            Document RCfile = null;
            ElementCategoryFilter linkedFileFilter = new ElementCategoryFilter(BuiltInCategory.OST_RvtLinks);
            FilteredElementCollector linkedFileCollector = new FilteredElementCollector(doc).WherePasses(linkedFileFilter).WhereElementIsNotElementType();
            if (linkedFileCollector.Count() > 0)
            {
                foreach (RevitLinkInstance linkedInst in linkedFileCollector)
                {
                    Document linkDoc = linkedInst.GetLinkDocument();
                    if (linkDoc == null || !linkedInst.IsValidObject) continue;
                    FilteredElementCollector linkedBeams = new FilteredElementCollector(linkDoc).OfClass(typeof(Instance)).OfCategory(BuiltInCategory.OST_StructuralFraming);
                    if (linkedBeams.Count() == 0) continue;
                    foreach (Element e in linkedBeams)
                    {
                        FamilyInstance beamInstance = e as FamilyInstance;
                        if (beamInstance.StructuralMaterialType.ToString() == "Steel" && !SClinkedInstances.Contains(linkedInst))
                        {
                            //RCfile = linkDoc;
                            SClinkedInstances.Add(linkedInst);
                        }
                    }
                }
            }
            else if (linkedFileCollector.Count() == 0)
            {
                MessageBox.Show("模型中沒有實做的外參檔案");
            }
            return SClinkedInstances;
        }
        public List<RevitLinkInstance> getLinkedInstances(Document doc, string materialName)
        {
            List<RevitLinkInstance> targetLinkedInstances = new List<RevitLinkInstance>();
            Document RCfile = null;
            ElementCategoryFilter linkedFileFilter = new ElementCategoryFilter(BuiltInCategory.OST_RvtLinks);
            FilteredElementCollector linkedFileCollector = new FilteredElementCollector(doc).WherePasses(linkedFileFilter).WhereElementIsNotElementType();
            try
            {
                if (linkedFileCollector.Count() > 0)
                {
                    foreach (RevitLinkInstance linkedInst in linkedFileCollector)
                    {
                        Document linkDoc = linkedInst.GetLinkDocument();
                        bool isLoaded = RevitLinkType.IsLoaded(doc, linkedInst.GetTypeId());
                        //if (linkDoc == null /*|| !linkedInst.IsValidObject*/ || !isLoaded) continue;
                        if (linkDoc != null && isLoaded)
                        {
                            FilteredElementCollector linkedBeams = new FilteredElementCollector(linkDoc).OfClass(typeof(Instance)).OfCategory(BuiltInCategory.OST_StructuralFraming);
                            if (linkedBeams.Count() == 0) continue;
                            foreach (Element e in linkedBeams)
                            {
                                FamilyInstance beamInstance = e as FamilyInstance;
                                if (beamInstance.StructuralMaterialType.ToString() == materialName && !targetLinkedInstances.Contains(linkedInst))
                                {
                                    //RCfile = linkDoc;
                                    targetLinkedInstances.Add(linkedInst);
                                }
                            }
                        }
                    }
                }
                else if (linkedFileCollector.Count() == 0)
                {
                    MessageBox.Show("模型中沒有實做的外參檔案");
                }
            }
            catch
            {
                MessageBox.Show("請檢查外參連結是否為載入或有問題!");
            }
            return targetLinkedInstances;
        }
        public RevitLinkInstance getTargetLinkedInstance(Document doc, string linkTilte)
        {
            RevitLinkInstance targetLinkInstance = null;
            ElementCategoryFilter linkedFileFilter = new ElementCategoryFilter(BuiltInCategory.OST_RvtLinks);
            FilteredElementCollector linkedFileCollector = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).WhereElementIsNotElementType();
            if (linkedFileCollector.Count() > 0)
            {
                foreach (RevitLinkInstance linkedInst in linkedFileCollector)
                {
                    //if (linkedInst.GetLinkDocument().Title == linkTilte)
                    if (linkedInst.Name.Contains(linkTilte))
                    {
                        targetLinkInstance = linkedInst;
                        break;
                    }
                }
            }
            else if (targetLinkInstance == null)
            {
                MessageBox.Show("未找到對應的實做Revit外參檔!!");
            }
            return targetLinkInstance;
        }
        public Document getLinkedSCDoc(Document doc)
        {
            Document SCfile = null;
            ElementCategoryFilter linkedFileFilter = new ElementCategoryFilter(BuiltInCategory.OST_RvtLinks);
            FilteredElementCollector linkedFileCollector = new FilteredElementCollector(doc).WherePasses(linkedFileFilter).WhereElementIsNotElementType();
            if (linkedFileCollector.Count() > 0)
            {
                foreach (RevitLinkInstance linkedInst in linkedFileCollector)
                {
                    Document linkDoc = linkedInst.GetLinkDocument();
                    FilteredElementCollector linkedBeams = new FilteredElementCollector(linkDoc).OfClass(typeof(Instance)).OfCategory(BuiltInCategory.OST_StructuralFraming);
                    foreach (Element e in linkedBeams)
                    {
                        FamilyInstance beamInstance = e as FamilyInstance;
                        if (beamInstance.StructuralMaterialType.ToString() == "Steel")
                        {
                            SCfile = linkDoc;
                        }
                    }
                }
            }
            else if (linkedFileCollector.Count() == 0)
            {
                MessageBox.Show("模型中沒有實做的外參檔案");
            }
            return SCfile;
        }
        public Parameter getBeamWidthPara(Element beam)
        {
            Parameter targetPara = null;
            FamilyInstance beamInst = beam as FamilyInstance;
            //因為樑寬度為類型參數
            double val1 = 0.0;
            double val2 = 0.0;
            if (checkPara(beamInst.Symbol, "梁寬度"))
            {
                val1 = beamInst.Symbol.LookupParameter("梁寬度").AsDouble();
            }
            if (checkPara(beamInst.Symbol, "樑寬"))
            {
                val2 = beamInst.Symbol.LookupParameter("樑寬").AsDouble();
            }
            if (val1 >= val2)
            {
                targetPara = beamInst.Symbol.LookupParameter("梁寬度");
            }
            else if (val1 <= val2)
            {
                targetPara = beamInst.Symbol.LookupParameter("樑寬");
            }
            else if (targetPara == null)
            {
                MessageBox.Show("請檢察樑中的「寬度」參數是否有誤，無法更新套管長度!");
            }
            return targetPara;
        }
        public FilteredElementCollector getAllLinkedBeam(Document linkedDoc)
        {
            FilteredElementCollector beamCollector = new FilteredElementCollector(linkedDoc).OfClass(typeof(Instance)).OfCategory(BuiltInCategory.OST_StructuralFraming);
            beamCollector.WhereElementIsNotElementType();
            return beamCollector;
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
        public List<FamilyInstance> findTargetElements(Document doc)
        {
            //RC套管跟SC開口的內部名稱是不同的
            string internalNameST = "CEC-穿樑開口";
            string internalNameRC = "CEC-穿樑套管";
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
                    Parameter p = e.Symbol.LookupParameter("API識別名稱");
                    if (p != null && p.AsString().Contains(internalNameST))
                    {
                        castInstances.Add(e);
                    }
                    else if (p != null && p.AsString().Contains(internalNameRC))
                    {
                        castInstances.Add(e);
                    }
                }
            }
            else if (castInstances.Count() == 0)
            {
                {
                    MessageBox.Show("尚未匯入套管元件，或模型中沒有實做的套管元件");
                }
            }
            return castInstances;
        }

        //可以判斷樑中樑的程式-->用Dictionary裝穿樑套管以及與之對應的樑
        //因為樑與套管正常來說應該是一對一的關係，強制取得它們的關係(用套管ID反查干涉的樑)
        public Dictionary<ElementId, List<Element>> getCastBeamDict(Document doc)
        {

            Dictionary<ElementId, List<Element>> castBeamDict = new Dictionary<ElementId, List<Element>>();
            List<Element> targetBeams = new List<Element>();
            List<FamilyInstance> familyInstances = findTargetElements(doc);
            //List<RevitLinkInstance> SCLinkedInstance = getSCLinkedInstances(doc);
            //List<RevitLinkInstance> RCLinkedInstance = getRCLinkedInstances(doc);
            List<RevitLinkInstance> SCLinkedInstance = getLinkedInstances(doc, "Steel");
            List<RevitLinkInstance> RCLinkedInstance = getLinkedInstances(doc, "Concrete");
            Transform totalTransform = null;


            if (RCLinkedInstance.Count != 0 || SCLinkedInstance.Count != 0)
            {
                foreach (FamilyInstance inst in familyInstances)
                {
                    //將RC和ST的檔案分別與inst去做碰撞，取得有用效的樑，再用dictionary的key值判斷套管是否已經存在字典之中，有才進行執行
                    foreach (RevitLinkInstance SClinkedInst in SCLinkedInstance)
                    {
                        //這個套管還沒有對應的樑(不再字典的key值中)才進行計算
                        if (!castBeamDict.Keys.Contains(inst.Id))
                        {
                            totalTransform = SClinkedInst.GetTotalTransform();
                            FilteredElementCollector collectorSC = getAllLinkedBeam(SClinkedInst.GetLinkDocument());
                            Solid castSolid = singleSolidFromElement(inst);
                            if (castSolid == null) continue;
                            BoundingBoxXYZ castBounding = inst.get_BoundingBox(null);
                            Transform t = castBounding.Transform;
                            Outline outLine = new Outline(t.OfPoint(castBounding.Min), t.OfPoint(castBounding.Max));
                            //Outline outLine = new Outline(castBounding.Min, castBounding.Max);
                            BoundingBoxIntersectsFilter boundingBoxIntersectsFilter = new BoundingBoxIntersectsFilter(outLine);
                            ElementIntersectsSolidFilter elementIntersectsSolidFilter = new ElementIntersectsSolidFilter(castSolid);
                            collectorSC.WherePasses(boundingBoxIntersectsFilter).WherePasses(elementIntersectsSolidFilter);

                            List<Element> tempList = collectorSC.ToList();
                            if (tempList.Count > 0)
                            {
                                castBeamDict.Add(inst.Id, tempList);
                            }
                        }
                    }
                    //和SC沒撞出東西，再和RC撞，如果被撞到的套管ID已在字典Key裡，則略過
                    foreach (RevitLinkInstance RClinkedInst in RCLinkedInstance)
                    {
                        if (!castBeamDict.Keys.Contains(inst.Id))
                        {
                            totalTransform = RClinkedInst.GetTotalTransform();
                            FilteredElementCollector collectorSC = getAllLinkedBeam(RClinkedInst.GetLinkDocument());
                            Solid castSolid = singleSolidFromElement(inst);
                            if (castSolid == null) continue;
                            BoundingBoxXYZ castBounding = inst.get_BoundingBox(null);
                            Transform t = castBounding.Transform;
                            Outline outLine = new Outline(t.OfPoint(castBounding.Min), t.OfPoint(castBounding.Max));
                            //Outline outLine = new Outline(castBounding.Min, castBounding.Max);
                            BoundingBoxIntersectsFilter boundingBoxIntersectsFilter = new BoundingBoxIntersectsFilter(outLine);
                            ElementIntersectsSolidFilter elementIntersectsSolidFilter = new ElementIntersectsSolidFilter(castSolid);
                            collectorSC.WherePasses(boundingBoxIntersectsFilter).WherePasses(elementIntersectsSolidFilter);

                            List<Element> tempList = collectorSC.ToList();
                            if (tempList.Count > 0)
                            {
                                castBeamDict.Add(inst.Id, tempList);
                            }
                        }
                    }
                }
            }
            return castBeamDict;
        }
        public bool checkGrider(Element elem)
        {
            bool result = false;
            Document doc = elem.Document;
            FilteredElementCollector tempCollector = new FilteredElementCollector(doc).OfClass(typeof(Instance)).OfCategory(BuiltInCategory.OST_StructuralColumns);
            BoundingBoxXYZ checkBounding = elem.get_BoundingBox(null);
            Autodesk.Revit.DB.Transform t1 = checkBounding.Transform;
            Outline outline1 = new Outline(t1.OfPoint(checkBounding.Min), t1.OfPoint(checkBounding.Max));
            BoundingBoxIntersectsFilter boundingBoxIntersectsFilter1 = new BoundingBoxIntersectsFilter(outline1, 0.1);
            //ElementIntersectsSolidFilter elementIntersectsSolidFilter1 = new ElementIntersectsSolidFilter(RCSolid);
            tempCollector.WherePasses(boundingBoxIntersectsFilter1);
            if (tempCollector.Count() > 0)
            {
                result = true;
            }
            else if (tempCollector.Count() == 0)
            {
                result = false;
            }
            return result;
        }
        public double getCastWidth(Element elem)
        {
            double targetWidth = 0.0;
            Parameter widthPara = null;
            FamilyInstance inst = elem as FamilyInstance;
            string inernalName = inst.Symbol.LookupParameter("API識別名稱").AsString();
            if (inernalName == null)
            {
                MessageBox.Show("請檢查目標元件的API識別名稱是否遭到修改");
            }
            else if (inernalName.Contains("圓"))
            {
                widthPara = inst.Symbol.LookupParameter("管外直徑");
                if (widthPara == null) MessageBox.Show($"請檢查{inst.Symbol.FamilyName}中是否缺少「管外直徑」參數欄位");
                targetWidth = widthPara.AsDouble();
            }
            else if (inernalName.Contains("方"))
            {
                widthPara = inst.LookupParameter("W");
                if (widthPara == null) MessageBox.Show($"請檢查{inst.Symbol.FamilyName}中是否缺少「W」參數欄位");
                targetWidth = widthPara.AsDouble();
            }
            return targetWidth;
        }
        public double getCastHeight(Element elem)
        {
            double targetWidth = 0.0;
            Parameter widthPara = null;
            FamilyInstance inst = elem as FamilyInstance;
            string inernalName = inst.Symbol.LookupParameter("API識別名稱").AsString();
            if (inernalName == null)
            {
                MessageBox.Show("請檢查目標元件的API識別名稱是否遭到修改");
            }
            else if (inernalName.Contains("圓"))
            {
                widthPara = inst.Symbol.LookupParameter("管外直徑");
                if (widthPara == null) MessageBox.Show($"請檢查{inst.Symbol.FamilyName}中是否缺少「管外直徑」參數欄位");
                targetWidth = widthPara.AsDouble();
            }
            else if (inernalName.Contains("方"))
            {
                widthPara = inst.LookupParameter("H");
                if (widthPara == null) MessageBox.Show($"請檢查{inst.Symbol.FamilyName}中是否缺少「H」參數欄位");
                targetWidth = widthPara.AsDouble();
            }
            return targetWidth;
        }
        public static XYZ TransformPoint(XYZ point, Transform transform)
        {
            double x = point.X;
            double y = point.Y;
            double z = point.Z;
            XYZ val = transform.get_Basis(0);
            XYZ val2 = transform.get_Basis(1);
            XYZ val3 = transform.get_Basis(2);
            XYZ origin = transform.Origin;
            double xTemp = x * val.X + y * val2.X + z * val3.X + origin.X;
            double yTemp = x * val.Y + y * val2.Y + z * val3.Y + origin.Y;
            double zTemp = x * val.Z + y * val2.Z + z * val3.Z + origin.Z;
            return new XYZ(xTemp, yTemp, zTemp);
        }
        public Element modifyCastLen(Element elem, Element linkedBeam)
        {
            //這個功能應該還要視樑為RC還是SRC去決定寬度值
            //先利用linkedBeam反找revitLinkedInstance
            Document document = elem.Document;
            RevitLinkInstance targetLink = getTargetLinkedInstance(document, linkedBeam.Document.Title);
            Transform linkedInstTrans = targetLink.GetTotalTransform();

            //計算偏移值&設定長度
            LocationCurve beamLocate = linkedBeam.Location as LocationCurve;
            Curve beamCurve = beamLocate.Curve;
            XYZ startPoint = beamCurve.GetEndPoint(0);
            XYZ endPoint = beamCurve.GetEndPoint(1);
            startPoint = TransformPoint(startPoint, linkedInstTrans);
            endPoint = TransformPoint(endPoint, linkedInstTrans);
            endPoint = new XYZ(endPoint.X, endPoint.Y, startPoint.Z);
            Line tempCrv = Line.CreateBound(startPoint, endPoint);

            LocationPoint castLocate = elem.Location as LocationPoint;
            XYZ castPt = castLocate.Point;
            XYZ tempPt = new XYZ(castPt.X, castPt.Y, startPoint.Z);
            IntersectionResult intersectResult = tempCrv.Project(castPt);
            XYZ targetPoint = intersectResult.XYZPoint;
            targetPoint = new XYZ(targetPoint.X, targetPoint.Y, castPt.Z);
            XYZ positionChange = targetPoint - castPt;
            double castLength = getBeamWidthPara(linkedBeam).AsDouble() + 2 / 30.48;

            FamilyInstance updateCast = null;
            FamilyInstance inst = elem as FamilyInstance;
            Parameter instLenPara = inst.LookupParameter("L");
            double beamWidth = getBeamWidthPara(linkedBeam).AsDouble();
            //先調整套管位置
            if (!castPt.IsAlmostEqualTo(targetPoint))
            {
                ElementTransformUtils.MoveElement(document, inst.Id, positionChange);
            }
            //再調整套管長度
            if (instLenPara.AsDouble() < beamWidth)
            {
                instLenPara.Set(castLength);
            }
            updateCast = inst;
            return updateCast;
        }
        public Element updateCastInst(Element elem, Element linkedBeam)
        {
            FamilyInstance updateCast = null;
            FamilyInstance inst = elem as FamilyInstance;
            Solid beamSolid = singleSolidFromElement(linkedBeam);
            SolidCurveIntersectionOptions options = new SolidCurveIntersectionOptions();
            DisplayUnitType unitType = DisplayUnitType.DUT_MILLIMETERS;
            if (null != beamSolid)
            {
                LocationPoint instLocate = inst.Location as LocationPoint;
                double inst_CenterZ = (inst.get_BoundingBox(null).Max.Z + inst.get_BoundingBox(null).Min.Z) / 2;
                XYZ instPt = instLocate.Point;
                double normal_BeamHeight = UnitUtils.ConvertToInternalUnits(1500, unitType);
                XYZ inst_Up = new XYZ(instPt.X, instPt.Y, instPt.Z + normal_BeamHeight);
                XYZ inst_Dn = new XYZ(instPt.X, instPt.Y, instPt.Z - normal_BeamHeight);
                Curve instVerticalCrv = Autodesk.Revit.DB.Line.CreateBound(inst_Dn, inst_Up);
                //這邊用solid是因為怕有斜樑需要開口的問題，但斜樑的結構應力應該已經蠻集中的，不可以再開口
                SolidCurveIntersection intersection = beamSolid.IntersectWithCurve(instVerticalCrv, options);
                int intersectCount = intersection.SegmentCount;
                //針對有切割到的實體去做計算六個參數
                if (intersectCount > 0)
                {
                    string instInternalName = inst.Symbol.LookupParameter("API識別名稱").AsString();
                    //針對有交集的實體去做計算
                    inst.LookupParameter("【原則檢討】是否穿樑").Set("OK");
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
                    double TTOP_orgin = inst.LookupParameter("TTOP").AsDouble();
                    double BBOP_orgin = inst.LookupParameter("BBOP").AsDouble();
                    double beamHeight = intersect_UP.Z - intersect_DN.Z;
                    double test = beamHeight * 30.48;
                    double castHeight = cast_Max.Z - cast_Min.Z;

                    double TTOP_Check = Math.Round(UnitUtils.ConvertFromInternalUnits(TTOP_update, unitType), 1);
                    double TTOP_orginCheck = Math.Round(UnitUtils.ConvertFromInternalUnits(TTOP_orgin, unitType), 1);
                    double BBOP_Check = Math.Round(UnitUtils.ConvertFromInternalUnits(BBOP_update, unitType), 1);
                    double BBOP_orginCheck = Math.Round(UnitUtils.ConvertFromInternalUnits(BBOP_orgin, unitType), 1);

                    if (TTOP_Check != TTOP_orginCheck || BBOP_Check != BBOP_orginCheck)
                    {
                        inst.LookupParameter("TTOP").Set(TTOP_update);
                        inst.LookupParameter("BTOP").Set(BTOP_update);
                        inst.LookupParameter("TCOP").Set(TCOP_update);
                        inst.LookupParameter("BCOP").Set(BCOP_update);
                        inst.LookupParameter("TBOP").Set(TBOP_update);
                        inst.LookupParameter("BBOP").Set(BBOP_update);
                    }
                    //寫入樑編號與樑尺寸
                    string beamName = linkedBeam.LookupParameter("編號").AsString();
                    string beamSIze = linkedBeam.LookupParameter("類型").AsValueString();
                    Parameter instBeamNum = inst.LookupParameter("貫穿樑編號");
                    Parameter instBeamSize = inst.LookupParameter("貫穿樑尺寸");
                    if (beamName != null)
                    {
                        instBeamNum.Set(beamName);
                    }
                    else
                    {
                        instBeamNum.Set("無編號");
                    }
                    if (beamSIze != null)
                    {
                        instBeamSize.Set(beamSIze);
                    }
                    else
                    {
                        instBeamSize.Set("無尺寸");
                    }

                    //設定檢核結果，在此之前感覺需要做一個找到元件寬度的程式
                    double protectDistCheck = 0.0; //確認套管是否與保護層過近
                    double sizeMaxCheck = 0.0; //確認套管是否過大
                    double sizeMaxCheckW = 0.0;
                    double sizeMaxCheckD = 0.0;
                    double endDistCheck = 0.0; //確認套管是否與樑末端過近
                    bool isGrider = checkGrider(linkedBeam); //確認是否為大小樑，以此作為參數更新的依據

                    //依照是否為大小樑，更新參數依據
                    double C_distRatio = 0.0, C_protectRatio = 0.0, C_protectMin = 0.0, C_sizeRatio = 0.0, C_sizeMax = 0.0; ;
                    double R_distRatio = 0.0, R_protectRatio = 0.0, R_protectMin = 0.0, R_sizeRatioD = 0.0, R_sizeRatioW = 0.0;
                    if (isGrider)
                    {
                        C_distRatio = BeamCast_Settings.Default.cD1_Ratio;
                        C_protectRatio = BeamCast_Settings.Default.cP1_Ratio;
                        C_protectMin = BeamCast_Settings.Default.cP1_Min;
                        C_sizeRatio = BeamCast_Settings.Default.cMax1_Ratio;
                        C_sizeMax = BeamCast_Settings.Default.cMax1_Max;
                        R_distRatio = BeamCast_Settings.Default.rD1_Ratio;
                        R_protectRatio = BeamCast_Settings.Default.rP1_Ratio;
                        R_protectMin = BeamCast_Settings.Default.rP1_Min;
                        R_sizeRatioD = BeamCast_Settings.Default.rMax1_RatioD;
                        R_sizeRatioW = BeamCast_Settings.Default.rMax1_RatioW;
                    }
                    else if (!isGrider)
                    {
                        C_distRatio = BeamCast_Settings.Default.cD2_Ratio;
                        C_protectRatio = BeamCast_Settings.Default.cP2_Ratio;
                        C_protectMin = BeamCast_Settings.Default.cP2_Min;
                        C_sizeRatio = BeamCast_Settings.Default.cMax2_Ratio;
                        C_sizeMax = BeamCast_Settings.Default.cMax2_Max;
                        R_distRatio = BeamCast_Settings.Default.rD2_Ratio;
                        R_protectRatio = BeamCast_Settings.Default.rP2_Ratio;
                        R_protectMin = BeamCast_Settings.Default.rP2_Min;
                        R_sizeRatioD = BeamCast_Settings.Default.rMax2_RatioD;
                        R_sizeRatioW = BeamCast_Settings.Default.rMax2_RatioW;
                    }
                    List<double> parameter_Checklist = new List<double> { C_distRatio, C_protectRatio, C_sizeRatio, R_distRatio, R_protectRatio, R_sizeRatioD, R_sizeRatioW };
                    List<double> parameter_Checklist2 = new List<double> { C_protectMin, C_sizeMax, R_protectMin };


                    bool isCircleCast = instInternalName.Contains("圓");

                    //前面已經檢查過是否為大小樑，在此檢查是方孔還圓孔，比例係數不同
                    //如果套管為圓形
                    if (isCircleCast)
                    {
                        //上下邊距警告值
                        protectDistCheck = C_protectRatio * beamHeight;
                        double tempProtectValue = UnitUtils.ConvertToInternalUnits(C_protectMin, unitType);
                        if (tempProtectValue > protectDistCheck) protectDistCheck = tempProtectValue;

                        //最大尺寸警告值
                        sizeMaxCheck = C_sizeRatio * beamHeight;
                        double tempSizeValue = UnitUtils.ConvertToInternalUnits(C_sizeMax, unitType);
                        if (tempSizeValue < sizeMaxCheck && tempSizeValue != 0) sizeMaxCheck = tempSizeValue;

                        //樑兩端警告值
                        endDistCheck = C_distRatio * beamHeight;
                    }
                    //如果套管為方形
                    else if (!isCircleCast)
                    {
                        //上下邊距警告值
                        protectDistCheck = R_protectRatio * beamHeight;
                        double tempProtectValue = UnitUtils.ConvertToInternalUnits(R_protectMin, unitType);
                        if (tempProtectValue < protectDistCheck) protectDistCheck = tempProtectValue;

                        //最大尺寸警告值
                        sizeMaxCheckW = R_sizeRatioW * beamHeight;
                        sizeMaxCheckD = R_sizeRatioD * beamHeight;

                        //樑兩端警告值
                        endDistCheck = R_distRatio * beamHeight;
                    }

                    //檢查是否穿樑
                    List<double> updateParas = new List<double> { TTOP_update, BTOP_update, TCOP_update, BCOP_update, TBOP_update, BBOP_update };
                    foreach (double d in updateParas)
                    {
                        if (d < 0)
                        {
                            inst.LookupParameter("【原則檢討】是否穿樑").Set("不符合");
                            instBeamNum.Set("無編號");
                            instBeamSize.Set("無尺寸");
                        }
                    }
                    //檢查是否過大
                    Parameter sizeCheckPara = inst.LookupParameter("【原則檢討】尺寸檢討");
                    if (isCircleCast)
                    {

                        double castSize = getCastWidth(inst);
                        if (castSize > sizeMaxCheck) sizeCheckPara.Set("不符合");
                        else sizeCheckPara.Set("OK");
                    }
                    else if (!isCircleCast)
                    {
                        double castSizeW = getCastWidth(inst);
                        double castSizeD = getCastHeight(inst);
                        if (castSizeW > sizeMaxCheckW || castSizeD > sizeMaxCheckD) sizeCheckPara.Set("不符合");
                        else sizeCheckPara.Set("OK");
                    }

                    //檢查上下部包護層
                    Parameter protectionCheckPara_UP = inst.LookupParameter("【原則檢討】上部檢討");
                    Parameter protectionCheckPara_DN = inst.LookupParameter("【原則檢討】下部檢討");
                    if (TTOP_update < protectDistCheck)
                    {
                        protectionCheckPara_UP.Set("不符合");
                    }
                    else
                    {
                        protectionCheckPara_UP.Set("OK");
                    }
                    if (BBOP_update < protectDistCheck)
                    {
                        protectionCheckPara_DN.Set("不符合");
                    }
                    else
                    {
                        protectionCheckPara_DN.Set("OK");
                    }

                    //檢查套管是否離樑的兩端過近
                    Parameter endCheckPara = inst.LookupParameter("【原則檢討】樑端檢討");
                    LocationCurve tempLocateCrv = linkedBeam.Location as LocationCurve;
                    Curve targetCrv = tempLocateCrv.Curve;
                    XYZ tempStart = targetCrv.GetEndPoint(0);
                    XYZ tempEnd = targetCrv.GetEndPoint(1);
                    XYZ startPt = new XYZ(tempStart.X, tempStart.Y, instPt.Z);
                    XYZ endPt = new XYZ(tempEnd.X, tempEnd.Y, instPt.Z);
                    List<XYZ> points = new List<XYZ>() { startPt, endPt };
                    List<double> distLIst = new List<double>();
                    foreach (XYZ pt in points)
                    {
                        double distToBeamEnd = instPt.DistanceTo(pt);
                        distLIst.Add(distToBeamEnd);
                    }
                    if (distLIst.Min() - getCastWidth(elem) / 2 < endDistCheck)
                    {
                        endCheckPara.Set("不符合");
                    }
                    else if (distLIst.Min() - getCastHeight(elem) / 2 > endDistCheck)
                    {
                        endCheckPara.Set("OK");
                    }
                }
                else if (intersectCount == 0)
                {
                    inst.LookupParameter("【原則檢討】是否穿樑").Set("不符合");
                    inst.LookupParameter("貫穿樑編號").Set("無編號");
                    inst.LookupParameter("貫穿樑尺寸").Set("無尺寸");
                }

                //與其他穿樑套管之間的距離檢討
                List<FamilyInstance> tempList = findTargetElements(elem.Document);
                List<double> distList = new List<double>();
                double baseWidth = getCastWidth(elem);
                foreach (FamilyInstance e in tempList)
                {
                    //自己跟自己不用算
                    if (e.Id == elem.Id)
                    {
                        continue;
                    }
                    //同一層的才要進行距離檢討與計算
                    else if (e.LevelId == elem.LevelId)
                    {
                        double targetWidth = getCastWidth(e);
                        double distCheck = baseWidth + targetWidth;
                        LocationPoint baseLocate = elem.Location as LocationPoint;
                        XYZ basePt = baseLocate.Point;
                        LocationPoint targetLocate = e.Location as LocationPoint;
                        XYZ targetPt = targetLocate.Point;
                        XYZ adjustPt = new XYZ(targetPt.X, targetPt.Y, basePt.Z);
                        double dist = basePt.DistanceTo(adjustPt);
                        if (dist / 1.5 < distCheck)
                        {
                            distList.Add(dist);
                        }
                    }
                }
                if (distList.Count > 0)
                {
                    inst.LookupParameter("【原則檢討】邊距檢討").Set("不符合");
                }
                else
                {
                    inst.LookupParameter("【原則檢討】邊距檢討").Set("OK");
                }
                updateCast = inst;
            }
            else if (null == beamSolid)
            {
                MessageBox.Show($"來自{linkedBeam.Document.Title}，編號{linkedBeam.Id}的樑，無法創造一個完整的實體，因此無法更新該樑內的套管");
            }

            return updateCast;
        }
        public Element updateCastContent(Document doc, Element elem)
        {
            FamilyInstance updateCast = null;
            List<string> systemName = new List<string>() { "E", "T", "W", "P", "F", "A", "G" };
            FamilyInstance inst = elem as FamilyInstance;
            FilteredElementCollector pipeCollector = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_PipeCurves);
            BoundingBoxXYZ castBounding = inst.get_BoundingBox(null);
            Outline castOutline = new Outline(castBounding.Min, castBounding.Max);
            BoundingBoxIntersectsFilter boxIntersectsFilter = new BoundingBoxIntersectsFilter(castOutline);
            Solid castSolid = singleSolidFromElement(inst);
            ElementIntersectsSolidFilter solidFilter = new ElementIntersectsSolidFilter(castSolid);
            pipeCollector.WherePasses(boxIntersectsFilter).WherePasses(solidFilter);
            inst.LookupParameter("干涉管數量").Set(pipeCollector.Count());
            if (pipeCollector.Count() == 0)
            {
                inst.LookupParameter("系統別").Set("未指定");
            }
            //針對蒐集到的管去做系統別的更新
            if (pipeCollector.Count() == 1)
            {
                string pipeSystem = pipeCollector.First().get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM).AsValueString();
                string shortSystemName = pipeSystem.Substring(0, 1);//以開頭的前綴字抓系統縮寫
                if (systemName.Contains(shortSystemName))
                {
                    inst.LookupParameter("系統別").Set(shortSystemName);
                }
                else
                {
                    inst.LookupParameter("系統別").Set("未指定");
                }
            }
            //如果有共管的狀況
            else if (pipeCollector.Count() >= 2)
            {
                List<string> shortNameList = new List<string>();
                foreach (Element pipe in pipeCollector)
                {
                    string pipeSystem = pipe.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM).AsValueString();
                    string shortSystemName = pipeSystem.Substring(0, 1);//以開頭的前綴字抓系統縮寫
                    shortNameList.Add(shortSystemName);
                    List<string> newList = shortNameList.Distinct().ToList();
                    //就算共管，如果同系統還是得寫一樣的縮寫名稱
                    if (newList.Count() == 1)
                    {
                        inst.LookupParameter("系統別").Set(newList.First());
                    }
                    //如果為不同系統共管，則設為M
                    else if (newList.Count() > 1)
                    {
                        inst.LookupParameter("系統別").Set("M");
                    }
                }
            }
            updateCast = inst;
            return updateCast;
        }
    }
}
