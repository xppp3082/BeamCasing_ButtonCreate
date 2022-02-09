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

                //一些初始設定，套管名稱，檔案名稱，系統縮寫
                string CastName = "穿樑套管共用參數_通用模型";
                string RC_BeamsFileName = "GCE-施工-AR-低樓層";
                List<string> AllLinkName = new List<string>();
                List<string> systemName = new List<string>() { "E", "T", "W", "P", "F", "A", "G" };

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
                if (castInstances.Count() == 0)
                {
                    MessageBox.Show("尚未匯入套管元件，或模型中沒有實做的套管元件");
                    return Result.Failed;
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
                List<ElementId> Cast_tooClose = new List<ElementId>(); //存放離樑頂或樑底太近的套管
                List<ElementId> Cast_tooBig = new List<ElementId>(); //存放太大的套管
                List<ElementId> Cast_Conflict = new List<ElementId>(); //存放彼此太過靠近的套管
                List<ElementId> Cast_BeamConfilct = new List<ElementId>(); //存放大樑兩端過近的套管
                List<ElementId> Cast_OtherConfilct = new List<ElementId>(); //存放小樑兩端過近的套管
                List<ElementId> Cast_Empty = new List<ElementId>();//存放空管的穿樑套管

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

                //找出doc中所有的外參樑_方法2
                ElementCategoryFilter linkedFileFilter = new ElementCategoryFilter(BuiltInCategory.OST_RvtLinks);
                FilteredElementCollector linkedFileCollector = new FilteredElementCollector(doc).WherePasses(linkedFileFilter).WhereElementIsNotElementType();
                RevitLinkInstance targetLinkInstance = null;

                //將UI視窗實做出來
                CastInformUpdateUI updateWindow = new CastInformUpdateUI(commandData);
                updateWindow.Show();

                //當有外參鋼構與外參RC時，先判斷是要找哪一個去做碰撞
                if (linkedFileCollector.Count() > 0)
                {
                    foreach (RevitLinkInstance linkedInst in linkedFileCollector)
                    {
                        AllLinkName.Add(linkedInst.GetLinkDocument().Title);
                        if (linkedInst.GetLinkDocument().Title.Equals(RC_BeamsFileName))
                        {
                            targetLinkInstance = linkedInst;
                        }
                    }
                }
                //找到特定連結模型檔案中的RC結構體模型
                ElementCategoryFilter BeamsFilter = new ElementCategoryFilter(BuiltInCategory.OST_StructuralFraming);
                ElementClassFilter instanceFilter = new ElementClassFilter(typeof(FamilyInstance));
                StructuralMaterialTypeFilter STFilter = new StructuralMaterialTypeFilter(StructuralMaterialType.Concrete);
                FilteredElementCollector RCBeamsCollector = new FilteredElementCollector(targetLinkInstance.GetLinkDocument()).WherePasses(BeamsFilter).WherePasses(instanceFilter).WherePasses(STFilter).WhereElementIsNotElementType();
                Transform toTrans = targetLinkInstance.GetTotalTransform();


                using (Transaction trans = new Transaction(doc))
                {
                    trans.Start("更新穿樑套管資訊");
                    foreach (Element e in RCBeamsCollector)
                    {
                        Solid solid = singleSolidFromElement(e);
                        //solid的座標變換，如果今天外參檔被移動過了，則需要在進行transform
                        if (!toTrans.IsIdentity)
                        {
                            solid = SolidUtils.CreateTransformed(solid, toTrans);
                        }
                        if (null != solid)
                        {
                            //用每個被實做出來的套管針對這隻樑進行檢查
                            //針對有交集的套管，計算TOP和BOP是否相同
                            //針對不同的，加入他們的ID
                            SolidCurveIntersectionOptions options = new SolidCurveIntersectionOptions();
                            foreach (FamilyInstance inst in castInstances)
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
                                    //double castHeight = cast_Max.Z - cast_Min.Z;
                                    double castHeight = inst.Symbol.LookupParameter("管外直徑").AsDouble();

                                    double TTOP_Check = Math.Round(UnitUtils.ConvertFromInternalUnits(TTOP_update, unitType), 1);
                                    double TTOP_orginCheck = Math.Round(UnitUtils.ConvertFromInternalUnits(TTOP_orgin, unitType), 1);

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
                                    if (TTOP_update < alertValue || BBOP_update < alertValue)
                                    {
                                        Cast_tooClose.Add(inst.Id);
                                    }
                                    //太大的套管
                                    double alertMaxSize = beamHeight / 3;//設定1/3的尺寸警告
                                    if (castHeight > alertMaxSize)
                                    {
                                        Cast_tooBig.Add(inst.Id);
                                    }

                                    //距離大樑(StructuralType=Beam) 或小樑(StructuralType=Other) 太近的套管
                                    //先判斷是方開口還是圓開口(距離不一樣)
                                    FamilyInstance beamInst = (FamilyInstance)e;
                                    LocationCurve tempLocateCrv = null;
                                    Curve targetCrv = null;
                                    XYZ startPt = null;
                                    XYZ endPt = null;
                                    List<XYZ> points = new List<XYZ>();
                                    string BeamUsage = beamInst.StructuralUsage.ToString();

                                    if (BeamUsage == "Girder")
                                    {
                                        tempLocateCrv = beamInst.Location as LocationCurve;
                                        targetCrv = tempLocateCrv.Curve;
                                        XYZ tempStart = targetCrv.GetEndPoint(0);
                                        XYZ tempEnd = targetCrv.GetEndPoint(1);
                                        startPt = new XYZ(tempStart.X, tempStart.Y, instPt.Z);
                                        endPt = new XYZ(tempEnd.X, tempStart.Y, instPt.Z);
                                        points.Add(startPt);
                                        points.Add(endPt);
                                        foreach (XYZ pt in points)
                                        {
                                            double distToBeamEnd = instPt.DistanceTo(pt);
                                            if (distToBeamEnd < beamHeight)
                                            {
                                                Cast_BeamConfilct.Add(inst.Id);
                                            }
                                        }
                                    }
                                    else if (BeamUsage == "Other")
                                    {
                                        tempLocateCrv = beamInst.Location as LocationCurve;
                                        targetCrv = tempLocateCrv.Curve;
                                        XYZ tempStart = targetCrv.GetEndPoint(0);
                                        XYZ tempEnd = targetCrv.GetEndPoint(1);
                                        startPt = new XYZ(tempStart.X, tempStart.Y, instPt.Z);
                                        endPt = new XYZ(tempEnd.X, tempStart.Y, instPt.Z);
                                        points.Add(startPt);
                                        points.Add(endPt);
                                        foreach (XYZ pt in points)
                                        {
                                            double distToBeamEnd = instPt.DistanceTo(pt);
                                            if (distToBeamEnd - castHeight / 2 < beamHeight)
                                            {
                                                Cast_OtherConfilct.Add(inst.Id);
                                            }
                                        }
                                    }

                                    //離別人太近的套管
                                    //(在原本的list中去除自己，逐個量測距離，取出彼此靠太近的套管，去除重複ID後，由大到小排序
                                    int index = castInstances.IndexOf(inst);
                                    for (int i = 0; i < castInstances.Count(); i++)
                                    {
                                        if (i == index)
                                        {
                                            continue;
                                        }
                                        else
                                        {
                                            //利用嚴謹的方法求取距離
                                            //如果是圓形的套管
                                            if (inst.Symbol.FamilyName == CastName)
                                            {
                                                double Dia1 = inst.Symbol.LookupParameter("管外直徑").AsDouble();
                                                double Dia2 = castInstances[i].Symbol.LookupParameter("管外直徑").AsDouble();
                                                LocationPoint thisLocation = inst.Location as LocationPoint;
                                                LocationPoint otherLocation = castInstances[i].Location as LocationPoint;
                                                XYZ thisPt = thisLocation.Point;
                                                XYZ otherPt = otherLocation.Point;
                                                double distBetween = thisPt.DistanceTo(otherPt);
                                                if (distBetween < (Dia1 + Dia2) * 1.5)
                                                {
                                                    Cast_Conflict.Add(castInstances[i].Id);
                                                }
                                                //MessageBox.Show(Dia1.ToString());
                                            }
                                        }

                                    }
                                }
                            }
                        }
                    }
                    //針對套管去做更新：貫穿管的系統別、貫穿的管隻數
                    using (SubTransaction subTrans = new SubTransaction(doc))
                    {
                        subTrans.Start();
                        foreach (FamilyInstance inst in castInstances)
                        {
                            FilteredElementCollector pipeCollector = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_PipeCurves);
                            BoundingBoxXYZ castBounding = inst.get_BoundingBox(null);
                            Outline castOutline = new Outline(castBounding.Min, castBounding.Max);
                            BoundingBoxIntersectsFilter boxIntersectsFilter = new BoundingBoxIntersectsFilter(castOutline);
                            Solid castSolid = singleSolidFromElement(inst);
                            ElementIntersectsSolidFilter solidFilter = new ElementIntersectsSolidFilter(castSolid);
                            pipeCollector.WherePasses(boxIntersectsFilter).WherePasses(solidFilter);
                            if (pipeCollector.Count() == 0)
                            {
                                Cast_Empty.Add(inst.Id);
                            }
                            inst.LookupParameter("干涉管數量").Set(pipeCollector.Count());

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
                        }
                        subTrans.Commit();
                    }
                    trans.Commit();
                }

                //設定Source源
                if (Cast_tooClose.Count > 0)
                {
                    updateWindow.ProtectConflictListBox.ItemsSource = Cast_tooClose;
                }
                else
                {
                    updateWindow.ProtectConflictListBox.ItemsSource="無";
                }
                if (Cast_Conflict.Count > 0)
                {
                    updateWindow.TooCloseCastListBox.ItemsSource = Cast_Conflict;
                }
                else
                {
                    updateWindow.TooCloseCastListBox.ItemsSource = "無";
                }

                if (Cast_tooBig.Count > 0)
                {
                    updateWindow.TooBigCastListBox.ItemsSource = Cast_tooBig;
                }
                else
                {
                    updateWindow.TooBigCastListBox.ItemsSource = "無";
                }

                if (Cast_OtherConfilct.Count > 0)
                {
                    updateWindow.OtherCastListBox.ItemsSource = Cast_OtherConfilct;
                }
                else
                {
                    updateWindow.OtherCastListBox.ItemsSource = "無";
                }

                if (Cast_BeamConfilct.Count > 0)
                {
                    updateWindow.GriderCastListBox.ItemsSource = Cast_BeamConfilct;
                }
                else
                {
                    updateWindow.GriderCastListBox.ItemsSource = "無";
                }

                if (Cast_Empty.Count > 0)
                {
                    updateWindow.EmptyCastListBox.ItemsSource = Cast_Empty;
                }
                else
                {
                    updateWindow.EmptyCastListBox.ItemsSource = "無";
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
                //else if (Cast_tooClose.Count() > 0 || Cast_tooBig.Count() > 0 || Cast_Conflict.Count() > 0 || Cast_OtherConfilct.Count() > 0 || Cast_BeamConfilct.Count() > 0)
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
                //    output2 += $"\n在小樑中，且離其他樑太近的套管有{Cast_OtherConfilct.Count()}個，ID如下—\n";
                //    foreach (Element e in Cast_OtherConfilct)
                //    {
                //        output2 += $"{e.Id};";
                //    }
                //    output2 += $"\n在大樑中，且離兩端柱太近的套管有{Cast_BeamConfilct.Count()}個，ID如下—\n";
                //    foreach (Element e in Cast_BeamConfilct)
                //    {
                //        output2 += $"{e.Id};";
                //    }

                //    MessageBox.Show(output2, "CEC-MEP", MessageBoxButtons.OKCancel);

                //}
                //else if (updateCastNum == 0)
                //{
                //    MessageBox.Show("所有套管資訊都已更新完畢!!", "CEC-MEP", MessageBoxButtons.OKCancel);
                //}
                //MessageBox.Show($"這個模型中有{castInstances.Count()}個實做的穿樑套管");
            }
            catch
            {
                //如果執行失敗，先檢查元件名稱，以及是否有BTOP、BCOP、BBOP、TTOP、TCOP、TBOP等六個參數
                //進一步檢查是否有干涉管數量、系統別、貫穿樑尺寸、貫穿樑編號
                message = "執行失敗，請檢查元件版次與參數是否遭到更改!";
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
            IList<Element> SRCcollector = collector.WherePasses(RC_boundFilter).WherePasses(RC_solidFIlter).ToElements();

            return SRCcollector;
        }

    }

    public class UpdateCast
    {
        //試著做到幾件事，取得要進行碰撞的所有外參樑
        //進行碰撞後更新
        //錯誤的匯出報表
        DisplayUnitType unitType = DisplayUnitType.DUT_MILLIMETERS;
        string targetLinkFileName = "GCE-施工-AR-低樓層";
        string CastName = "穿樑套管共用參數_通用模型";
        List<string> AllLinkName = new List<string>();
        List<string> systemName = new List<string>() { "E", "T", "W", "P", "F", "A", "G" };
        Document doc;
        Transform targetTrans;

        //建置蒐集特例的List
        int updateCastNum = 0;
        List<Element> intersectInst = new List<Element>();
        public List<ElementId> updateCastIDs = new List<ElementId>();
        public List<Element> Cast_tooClose = new List<Element>(); //存放離樑頂或樑底太近的套管
        public List<Element> Cast_tooBig = new List<Element>(); //存放太大的套管
        public List<Element> Cast_Conflict = new List<Element>(); //存放彼此太過靠近的套管
        public List<Element> Cast_BeamConfilct = new List<Element>(); //存放大樑兩端過近的套管
        public List<Element> Cast_OtherConfilct = new List<Element>(); //存放小樑兩端過近的套管
        public UpdateCast(Document doc)
        {
            this.doc = doc;
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
            //var newSolid = SolidUtils.CreateTransformed(solidResult, tr);
            //return newSolid;
            return solidResult;
        }
        //初始化需要用到的doc

        public List<FamilyInstance> getAllCastInst()
        {
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
            return castInstances;
        }
        public FilteredElementCollector getAllLinkedBeam()
        {
            //找出doc中所有的外參樑_方法
            ElementCategoryFilter linkedFileFilter = new ElementCategoryFilter(BuiltInCategory.OST_RvtLinks);
            FilteredElementCollector linkedFileCollector = new FilteredElementCollector(doc).WherePasses(linkedFileFilter).WhereElementIsNotElementType();
            RevitLinkInstance targetLinkInstance = null;

            //當有外參鋼構與外參RC時，先判斷是要找哪一個去做碰撞
            if (linkedFileCollector.Count() > 0)
            {
                foreach (RevitLinkInstance linkedInst in linkedFileCollector)
                {
                    AllLinkName.Add(linkedInst.GetLinkDocument().Title);
                    if (linkedInst.GetLinkDocument().Title.Equals(targetLinkFileName))
                    {
                        targetLinkInstance = linkedInst;
                    }
                }
            }
            //找到特定連結模型檔案中的RC結構體模型
            ElementCategoryFilter BeamsFilter = new ElementCategoryFilter(BuiltInCategory.OST_StructuralFraming);
            ElementClassFilter instanceFilter = new ElementClassFilter(typeof(FamilyInstance));
            StructuralMaterialTypeFilter STFilter = new StructuralMaterialTypeFilter(StructuralMaterialType.Concrete);
            FilteredElementCollector RCBeamsCollector = new FilteredElementCollector(targetLinkInstance.GetLinkDocument()).WherePasses(BeamsFilter).WherePasses(instanceFilter).WherePasses(STFilter).WhereElementIsNotElementType();
            Transform toTrans = targetLinkInstance.GetTotalTransform();
            targetTrans = toTrans;
            return RCBeamsCollector;
        }
        public void findIntersectAndUpdate()
        {
            try
            {
                using (Transaction trans = new Transaction(doc))
                {
                    trans.Start("更新穿樑套管資訊");
                    //List<FamilyInstance> castInstances = getAllCastInst();
                    //FilteredElementCollector RCBeamsCollector = getAllLinkedBeam();
                    //RCBeamsCollector.Count();
                    //foreach (Element e in RCBeamsCollector)
                    //{
                    //    Solid solid = singleSolidFromElement(e);
                    //    //solid的座標變換，如果今天外參檔被移動過了，則需要在進行transform
                    //    if (!targetTrans.IsIdentity)
                    //    {
                    //        solid = SolidUtils.CreateTransformed(solid, targetTrans);
                    //    }
                    //    if (null != solid)
                    //    {
                    //        //用每個被實做出來的套管針對這隻樑進行檢查
                    //        //針對有交集的套管，計算TOP和BOP是否相同
                    //        //針對不同的，加入他們的ID
                    //        SolidCurveIntersectionOptions options = new SolidCurveIntersectionOptions();
                    //        foreach (FamilyInstance inst in castInstances)
                    //        {
                    //            LocationPoint instLocate = inst.Location as LocationPoint;
                    //            double inst_CenterZ = (inst.get_BoundingBox(null).Max.Z + inst.get_BoundingBox(null).Min.Z) / 2;
                    //            XYZ instPt = instLocate.Point;
                    //            double normal_BeamHeight = UnitUtils.ConvertToInternalUnits(1000, unitType);
                    //            XYZ inst_Up = new XYZ(instPt.X, instPt.Y, instPt.Z + normal_BeamHeight);
                    //            XYZ inst_Dn = new XYZ(instPt.X, instPt.Y, instPt.Z - normal_BeamHeight);
                    //            Curve instVerticalCrv = Line.CreateBound(inst_Dn, inst_Up);

                    //            //這邊用solid是因為怕有斜樑需要開口的問題，但斜樑的結構應力應該已經蠻集中的，不可以再開口
                    //            SolidCurveIntersection intersection = solid.IntersectWithCurve(instVerticalCrv, options);
                    //            int intersectCount = intersection.SegmentCount;


                    //            //針對有切割到的實體去做計算
                    //            if (intersectCount > 0)
                    //            {
                    //                //針對有交集的實體去做計算
                    //                intersectInst.Add(inst);

                    //                //計算TOP、BOP等六個參數
                    //                LocationPoint cast_Locate = inst.Location as LocationPoint;
                    //                XYZ LocationPt = cast_Locate.Point;
                    //                XYZ cast_Max = inst.get_BoundingBox(null).Max;
                    //                XYZ cast_Min = inst.get_BoundingBox(null).Min;
                    //                Curve castIntersect_Crv = intersection.GetCurveSegment(0);
                    //                XYZ intersect_DN = castIntersect_Crv.GetEndPoint(0);
                    //                XYZ intersect_UP = castIntersect_Crv.GetEndPoint(1);
                    //                double castCenter_Z = (cast_Max.Z + cast_Min.Z) / 2;

                    //                double TTOP_update = intersect_UP.Z - cast_Max.Z;
                    //                double BTOP_update = cast_Max.Z - intersect_DN.Z;
                    //                double TCOP_update = intersect_UP.Z - castCenter_Z;
                    //                double BCOP_update = castCenter_Z - intersect_DN.Z;
                    //                double TBOP_update = intersect_UP.Z - cast_Min.Z;
                    //                double BBOP_update = cast_Min.Z - intersect_DN.Z;
                    //                //MessageBox.Show($"交集的上緣Z值為:{intersect_UP.Z}，下緣Z值為:{intersect_DN.Z}，這個穿樑套管的Z值為{LocationPt.Z}");
                    //                double TTOP_orgin = inst.LookupParameter("TTOP").AsDouble();
                    //                double BBOP_orgin = inst.LookupParameter("BBOP").AsDouble();
                    //                double beamHeight = intersect_UP.Z - intersect_DN.Z;
                    //                //double castHeight = cast_Max.Z - cast_Min.Z;
                    //                double castHeight = inst.Symbol.LookupParameter("管外直徑").AsDouble();

                    //                double TTOP_Check = Math.Round(UnitUtils.ConvertFromInternalUnits(TTOP_update, unitType), 1);
                    //                double TTOP_orginCheck = Math.Round(UnitUtils.ConvertFromInternalUnits(TTOP_orgin, unitType), 1);

                    //                if (TTOP_Check != TTOP_orginCheck)
                    //                {
                    //                    inst.LookupParameter("TTOP").Set(TTOP_update);
                    //                    inst.LookupParameter("BTOP").Set(BTOP_update);
                    //                    inst.LookupParameter("TCOP").Set(TCOP_update);
                    //                    inst.LookupParameter("BCOP").Set(BCOP_update);
                    //                    inst.LookupParameter("TBOP").Set(TBOP_update);
                    //                    inst.LookupParameter("BBOP").Set(BBOP_update);
                    //                    updateCastNum += 1;
                    //                    Element updateElem = inst as Element;
                    //                    updateCastIDs.Add(updateElem.Id);
                    //                }

                    //                //寫入樑編號與量尺寸
                    //                string beamName = e.LookupParameter("編號").AsString();
                    //                string beamSIze = e.LookupParameter("類型").AsValueString();//抓取類型
                    //                if (beamName != null)
                    //                {
                    //                    inst.LookupParameter("貫穿樑編號").Set(beamName);
                    //                }
                    //                else
                    //                {
                    //                    inst.LookupParameter("貫穿樑編號").Set("無編號");
                    //                }
                    //                if (beamSIze != null)
                    //                {
                    //                    inst.LookupParameter("貫穿樑尺寸").Set(beamSIze);
                    //                }
                    //                else
                    //                {
                    //                    inst.LookupParameter("貫穿樑尺寸").Set("無尺寸");
                    //                }

                    //                //太過靠近樑底的套管
                    //                double alertValue = beamHeight / 4; //設定樑底與樑頂1/4的距離警告
                    //                if (TTOP_update < alertValue || BBOP_update < alertValue)
                    //                {
                    //                    Cast_tooClose.Add(inst);
                    //                }
                    //                //太大的套管
                    //                double alertMaxSize = beamHeight / 3;//設定1/3的尺寸警告
                    //                if (castHeight > alertMaxSize)
                    //                {
                    //                    Cast_tooBig.Add(inst);
                    //                }

                    //                //距離大樑(StructuralType=Beam) 或小樑(StructuralType=Other) 太近的套管
                    //                //先判斷是方開口還是圓開口(距離不一樣)
                    //                FamilyInstance beamInst = (FamilyInstance)e;
                    //                LocationCurve tempLocateCrv = null;
                    //                Curve targetCrv = null;
                    //                XYZ startPt = null;
                    //                XYZ endPt = null;
                    //                List<XYZ> points = new List<XYZ>();
                    //                string BeamUsage = beamInst.StructuralUsage.ToString();

                    //                if (BeamUsage == "Girder")
                    //                {
                    //                    tempLocateCrv = beamInst.Location as LocationCurve;
                    //                    targetCrv = tempLocateCrv.Curve;
                    //                    XYZ tempStart = targetCrv.GetEndPoint(0);
                    //                    XYZ tempEnd = targetCrv.GetEndPoint(1);
                    //                    startPt = new XYZ(tempStart.X, tempStart.Y, instPt.Z);
                    //                    endPt = new XYZ(tempEnd.X, tempStart.Y, instPt.Z);
                    //                    points.Add(startPt);
                    //                    points.Add(endPt);
                    //                    foreach (XYZ pt in points)
                    //                    {
                    //                        double distToBeamEnd = instPt.DistanceTo(pt);
                    //                        if (distToBeamEnd < beamHeight)
                    //                        {
                    //                            Cast_BeamConfilct.Add(inst);
                    //                        }
                    //                    }
                    //                }
                    //                else if (BeamUsage == "Other")
                    //                {
                    //                    tempLocateCrv = beamInst.Location as LocationCurve;
                    //                    targetCrv = tempLocateCrv.Curve;
                    //                    XYZ tempStart = targetCrv.GetEndPoint(0);
                    //                    XYZ tempEnd = targetCrv.GetEndPoint(1);
                    //                    startPt = new XYZ(tempStart.X, tempStart.Y, instPt.Z);
                    //                    endPt = new XYZ(tempEnd.X, tempStart.Y, instPt.Z);
                    //                    points.Add(startPt);
                    //                    points.Add(endPt);
                    //                    foreach (XYZ pt in points)
                    //                    {
                    //                        double distToBeamEnd = instPt.DistanceTo(pt);
                    //                        if (distToBeamEnd - castHeight / 2 < beamHeight)
                    //                        {
                    //                            Cast_OtherConfilct.Add(inst);
                    //                        }
                    //                    }
                    //                }

                    //                //離別人太近的套管
                    //                //(在原本的list中去除自己，逐個量測距離，取出彼此靠太近的套管，去除重複ID後，由大到小排序
                    //                int index = castInstances.IndexOf(inst);
                    //                for (int i = 0; i < castInstances.Count(); i++)
                    //                {
                    //                    if (i == index)
                    //                    {
                    //                        continue;
                    //                    }
                    //                    else
                    //                    {
                    //                        //利用嚴謹的方法求取距離
                    //                        //如果是圓形的套管
                    //                        if (inst.Symbol.FamilyName == CastName)
                    //                        {
                    //                            double Dia1 = inst.Symbol.LookupParameter("管外直徑").AsDouble();
                    //                            double Dia2 = castInstances[i].Symbol.LookupParameter("管外直徑").AsDouble();
                    //                            LocationPoint thisLocation = inst.Location as LocationPoint;
                    //                            LocationPoint otherLocation = castInstances[i].Location as LocationPoint;
                    //                            XYZ thisPt = thisLocation.Point;
                    //                            XYZ otherPt = otherLocation.Point;
                    //                            double distBetween = thisPt.DistanceTo(otherPt);
                    //                            if (distBetween < (Dia1 + Dia2) * 1.5)
                    //                            {
                    //                                Cast_Conflict.Add(castInstances[i]);
                    //                            }
                    //                            //MessageBox.Show(Dia1.ToString());
                    //                        }
                    //                    }
                    //                }
                    //            }
                    //        }
                    //    }
                    //}
                    ////針對套管去做更新：貫穿管的系統別、貫穿的管隻數
                    //using (SubTransaction subTrans = new SubTransaction(doc))
                    //{
                    //    subTrans.Start();
                    //    foreach (FamilyInstance inst in castInstances)
                    //    {
                    //        FilteredElementCollector pipeCollector = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_PipeCurves);
                    //        BoundingBoxXYZ castBounding = inst.get_BoundingBox(null);
                    //        Outline castOutline = new Outline(castBounding.Min, castBounding.Max);
                    //        BoundingBoxIntersectsFilter boxIntersectsFilter = new BoundingBoxIntersectsFilter(castOutline);
                    //        Solid castSolid = singleSolidFromElement(inst);
                    //        ElementIntersectsSolidFilter solidFilter = new ElementIntersectsSolidFilter(castSolid);
                    //        pipeCollector.WherePasses(boxIntersectsFilter).WherePasses(solidFilter);
                    //        pipeCollector.Count();
                    //        inst.LookupParameter("干涉管數量").Set(pipeCollector.Count());

                    //        //針對蒐集到的管去做系統別的更新
                    //        if (pipeCollector.Count() == 1)
                    //        {
                    //            string pipeSystem = pipeCollector.First().get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM).AsValueString();
                    //            string shortSystemName = pipeSystem.Substring(0, 1);//以開頭的前綴字抓系統縮寫
                    //            if (systemName.Contains(shortSystemName))
                    //            {
                    //                inst.LookupParameter("系統別").Set(shortSystemName);
                    //            }
                    //            else
                    //            {
                    //                inst.LookupParameter("系統別").Set("未指定");
                    //            }
                    //        }
                    //        //如果有共管的狀況
                    //        else if (pipeCollector.Count() >= 2)
                    //        {
                    //            List<string> shortNameList = new List<string>();
                    //            foreach (Element pipe in pipeCollector)
                    //            {
                    //                string pipeSystem = pipe.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM).AsValueString();
                    //                string shortSystemName = pipeSystem.Substring(0, 1);//以開頭的前綴字抓系統縮寫
                    //                shortNameList.Add(shortSystemName);
                    //                List<string> newList = shortNameList.Distinct().ToList();
                    //                //就算共管，如果同系統還是得寫一樣的縮寫名稱
                    //                if (newList.Count() == 1)
                    //                {
                    //                    inst.LookupParameter("系統別").Set(newList.First());
                    //                }
                    //                //如果為不同系統共管，則設為M
                    //                else if (newList.Count() > 1)
                    //                {
                    //                    inst.LookupParameter("系統別").Set("M");
                    //                }
                    //            }
                    //        }
                    //    }
                    //    subTrans.Commit();
                    //}
                    MessageBox.Show("測試成功");
                    trans.Commit();
                }
            }
            catch
            {
                //如果執行失敗，先檢查元件名稱，以及是否有BTOP、BCOP、BBOP、TTOP、TCOP、TBOP等六個參數
                //進一步檢查是否有干涉管數量、系統別、貫穿樑尺寸、貫穿樑編號
                MessageBox.Show("執行失敗，請檢查元件版次與參數是否遭到更改喔!");
            }
        }
    }
}

