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
        //���ձN��Lbutton�[��{��TAB
        const string RIBBON_TAB = "�iCEC MEP�j";
        const string RIBBON_PANEL = "��ٶ}�f";
        public Result OnStartup(UIControlledApplication a)
        {

            RibbonPanel targetPanel = null;
            // get the ribbon tab
            try
            {
                a.CreateRibbonTab(RIBBON_TAB);
            }catch (Exception) { } //tab alreadt exists
            RibbonPanel panel = null;
            List<RibbonPanel> panels = a.GetRibbonPanels(RIBBON_TAB); //�b���n�T�ORIBBON_TAB�b�o�椧�e�w�g�Q�Ы�
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
            System.Drawing.Image image_CreateST = Properties.Resources.��ٮM��ICON�X��_ST;
            ImageSource imgSrc0 = GetImageSource(image_CreateST);

            System.Drawing.Image image_Create = Properties.Resources.��ٮM��ICON�X��_RC;
            ImageSource imgSrc = GetImageSource(image_Create);


            System.Drawing.Image image_Update = Properties.Resources.��ٮM��ICON�X��_��s;
            ImageSource imgSrc2 = GetImageSource(image_Update);

            System.Drawing.Image image_SetUp = Properties.Resources.��ٮM��ICON�X��_�]�w;
            ImageSource imgSrc3 = GetImageSource(image_SetUp);

            // create the button data
            PushButtonData btnData0 = new PushButtonData(
             "MyButton_CastCreateST",
             "�Ы�\n   ST��ٮM��   ",
             Assembly.GetExecutingAssembly().Location,
             "BeamCasing_ButtonCreate.CreateBeamCastST"//���s�����W-->�n�̷ӻݭn�ѷӪ�command���J
             );
            {
                btnData0.ToolTip = "�I��~�ѼٻP�ޥͦ���ٶ}�f";
                btnData0.LongDescription = "���I��ݭn�Ыت��ެq�A�A�I����L���~�Ѽ١A�ͦ���ٮM��";
                btnData0.LargeImage = imgSrc0;
            };

            PushButtonData btnData = new PushButtonData(
                "MyButton_CastCreate",
                "�Ы�\n   RC��ٮM��   ",
                Assembly.GetExecutingAssembly().Location,
                "BeamCasing_ButtonCreate.CreateBeamCast"//���s�����W-->�n�̷ӻݭn�ѷӪ�command���J
                );
            {
                btnData.ToolTip = "�I��~�ѼٻP�ޥͦ���ٮM��";
                btnData.LongDescription = "���I��ݭn�Ыت��ެq�A�A�I����L���~�Ѽ١A�ͦ���ٮM��";
                btnData.LargeImage = imgSrc;
            };


            PushButtonData btnData2 = new PushButtonData(
                "MyButton_CastUpdate", 
                "��s\n   ��ٸ�T   ",
                Assembly.GetExecutingAssembly().Location,
                "BeamCasing_ButtonCreate.CastInfromUpdateV4"
                );
            {
                btnData2.ToolTip = "�@���s��ٮM�޻P��ٶ}�f��T";
                btnData2.LongDescription = "�̷ӥ��׳]�w������h�A��s��ٮM�޸�T (�������]�w��٭�h��i�ϥ�)";
                btnData2.LargeImage = imgSrc2;
            }

            PushButtonData btnData3 = new PushButtonData(
                "MyButton_CastSetUp", 
                "�]�w\n   ��٭�h   ",
                Assembly.GetExecutingAssembly().Location,
                "BeamCasing_ButtonCreate.BeamCastSetUp"
                );
            {
                btnData3.ToolTip = "�]�w��٭�h����";
                btnData3.LongDescription = "�̾ڱM�׻ݨD�A�]�w���ת���٭�h��T";
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
            //���TAB�W�٤���A�s�@button
            //foreach (adWin.RibbonTab tab in ribbon.Tabs)
            //{
            //    if (tab.Name == "�iCEC MEP�j")
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
            //�s�@�@��function�M���ӳB�z�Ϥ�
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
