#region Namespaces
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection; // for getting the assembly path
using System.Windows.Media; // for the graphics
using System.Windows.Media.Imaging;
using adWin = Autodesk.Windows;

#endregion

namespace BeamCasing_ButtonCreate
{

    class App : IExternalApplication
    {
        //測試將其他button加到現有TAB
        const string RIBBON_TAB = "【CEC MEP】";
        const string RIBBON_PANEL = "穿樑開口";
        public Result OnStartup(UIControlledApplication a)
        {

            RibbonPanel targetPanel = null;
            // get the ribbon tab
            try
            {
                a.CreateRibbonTab(RIBBON_TAB);
            }catch (Exception) { } //tab alreadt exists
            RibbonPanel panel = null;
            List<RibbonPanel> panels = a.GetRibbonPanels(RIBBON_TAB); //在此要確保RIBBON_TAB在這行之前已經被創建
            foreach (RibbonPanel pnl in panels)
            {
                if (pnl.Name == RIBBON_PANEL)
                {
                    panel = pnl;
                    break;
                }
            }
            // couldn't find panel, create it
            if (panel == null)
            {
                panel = a.CreateRibbonPanel(RIBBON_TAB, RIBBON_PANEL);
            }
            // get the image for the button
            System.Drawing.Image image_CreateST = Properties.Resources.穿樑套管ICON合集_ST;
            ImageSource imgSrc0 = GetImageSource(image_CreateST);

            System.Drawing.Image image_Create = Properties.Resources.穿樑套管ICON合集_RC;
            ImageSource imgSrc = GetImageSource(image_Create);


            System.Drawing.Image image_Update = Properties.Resources.穿樑套管ICON合集_更新;
            ImageSource imgSrc2 = GetImageSource(image_Update);

            System.Drawing.Image image_SetUp = Properties.Resources.穿樑套管ICON合集_設定;
            ImageSource imgSrc3 = GetImageSource(image_SetUp);

            // create the button data
            PushButtonData btnData0 = new PushButtonData(
             "MyButton_CastCreateST",
             "創建\n   ST穿樑套管   ",
             Assembly.GetExecutingAssembly().Location,
             "BeamCasing_ButtonCreate.CreateBeamCastST"//按鈕的全名-->要依照需要參照的command打入
             );
            {
                btnData0.ToolTip = "點選外參樑與管生成穿樑開口";
                btnData0.LongDescription = "先點選需要創建的管段，再點選其穿過的外參樑，生成穿樑套管";
                btnData0.LargeImage = imgSrc0;
            };

            PushButtonData btnData = new PushButtonData(
                "MyButton_CastCreate",
                "創建\n   RC穿樑套管   ",
                Assembly.GetExecutingAssembly().Location,
                "BeamCasing_ButtonCreate.CreateBeamCast"//按鈕的全名-->要依照需要參照的command打入
                );
            {
                btnData.ToolTip = "點選外參樑與管生成穿樑套管";
                btnData.LongDescription = "先點選需要創建的管段，再點選其穿過的外參樑，生成穿樑套管";
                btnData.LargeImage = imgSrc;
            };


            PushButtonData btnData2 = new PushButtonData(
                "MyButton_CastUpdate", 
                "更新\n   穿樑資訊   ",
                Assembly.GetExecutingAssembly().Location,
                "BeamCasing_ButtonCreate.CastInfromUpdateV4"
                );
            {
                btnData2.ToolTip = "一鍵更新穿樑套管與穿樑開口資訊";
                btnData2.LongDescription = "依照本案設定的穿梁原則，更新穿樑套管資訊 (必須先設定穿樑原則方可使用)";
                btnData2.LargeImage = imgSrc2;
            }

            PushButtonData btnData3 = new PushButtonData(
                "MyButton_CastSetUp", 
                "設定\n   穿樑原則   ",
                Assembly.GetExecutingAssembly().Location,
                "BeamCasing_ButtonCreate.BeamCastSetUp"
                );
            {
                btnData3.ToolTip = "設定穿樑原則限制";
                btnData3.LongDescription = "依據專案需求，設定本案的穿樑原則資訊";
                btnData3.LargeImage = imgSrc3;
            }
            PushButton button0 = panel.AddItem(btnData0) as PushButton;
            PushButton button = panel.AddItem(btnData) as PushButton;
            PushButton button2 = panel.AddItem(btnData2) as PushButton;
            PushButton button3 = panel.AddItem(btnData3) as PushButton;
            button0.Enabled = true;
            button.Enabled = true;
            button2.Enabled = true;
            button3.Enabled = true;

            //adWin.RibbonControl ribbon = adWin.ComponentManager.Ribbon;
            //找到TAB名稱之後再製作button
            //foreach (adWin.RibbonTab tab in ribbon.Tabs)
            //{
            //    if (tab.Name == "【CEC MEP】")
            //    {
            //        foreach (adWin.RibbonPanel panel in tab.Panels)
            //        {
            //            //if (panel.Source.Id == RIBBON_PANEL)
            //            //{
            //            //    targetPanel = panel;
            //            //    break;
            //            //}

            //        }

            //        //

            //    }
            //}




            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication a)
        {
            return Result.Succeeded;
        }

        private BitmapSource GetImageSource(Image img)
        {
            //製作一個function專門來處理圖片
            BitmapImage bmp = new BitmapImage();

            using (MemoryStream ms = new MemoryStream())
            {
                img.Save(ms, ImageFormat.Png);
                ms.Position = 0;

                bmp.BeginInit();

                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = null;
                bmp.StreamSource = ms;

                bmp.EndInit();
            }

            return bmp;
        }
    }
}
