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
        const string RIBBON_PANEL2 = "CSD&SEM";
        public Result OnStartup(UIControlledApplication a)
        {

            RibbonPanel targetPanel = null;
            // get the ribbon tab
            try
            {
                a.CreateRibbonTab(RIBBON_TAB);
            }
            catch (Exception) { } //tab alreadt exists
            RibbonPanel panel = null;
            //�Ыءu��ٮM�ޡv����
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

            //�ЫءuSEM&CSD�v����
            RibbonPanel panel2 = null;
            foreach (RibbonPanel pnl in panels)
            {
                if (pnl.Name == RIBBON_PANEL2)
                {
                    panel2 = pnl;
                    break;
                }
            }
            // couldn't find panel, create it
            if (panel2 == null)
            {
                panel2 = a.CreateRibbonPanel(RIBBON_TAB, RIBBON_PANEL2);
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


            System.Drawing.Image image_Num = Properties.Resources.��ٮM��ICON�X��_�s��2;
            ImageSource imgSrc4 = GetImageSource(image_Num);


            System.Drawing.Image image_ReNum = Properties.Resources.��ٮM��ICON�X��_���s��2;
            ImageSource imgSrc5 = GetImageSource(image_ReNum);

            System.Drawing.Image image_Copy = Properties.Resources.�Ƭ�ٮM��ICON�X��_�ƻs;
            ImageSource imgSrc6 = GetImageSource(image_Copy);


            // create the button data
            PushButtonData btnData0 = new PushButtonData(
             "MyButton_CastCreateST",
             "   ���c�}��   ",
             Assembly.GetExecutingAssembly().Location,
             "BeamCasing_ButtonCreate.CreateBeamCastSTV2"//���s�����W-->�n�̷ӻݭn�ѷӪ�command���J
             );
            {
                btnData0.ToolTip = "�I��~�ѼٻP�ޥͦ���ٶ}�f";
                btnData0.LongDescription = "���I��ݭn�Ыت��ެq�A�A�I����L���~�Ѽ١A�ͦ���ٮM��";
                btnData0.LargeImage = imgSrc0;
            };

            PushButtonData btnData = new PushButtonData(
                "MyButton_CastCreate",
                "   RC�M��   ",
                Assembly.GetExecutingAssembly().Location,
                "BeamCasing_ButtonCreate.CreateBeamCastV2"//���s�����W-->�n�̷ӻݭn�ѷӪ�command���J
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

            PushButtonData btnData4 = new PushButtonData(
    "MyButton_CastNum",
    "��ٮM��\n   �s��   ",
    Assembly.GetExecutingAssembly().Location,
    "BeamCasing_ButtonCreate.UpdateCastNumber"
    );
            {
                btnData4.ToolTip = "��ٮM�ަ۰ʽs��";
                btnData4.LongDescription = "�ھڨC�h�Ӫ��}�f�ƶq�P��m�A�̧Ǧ۰ʱa�J�s���A�ĤG���W�J�s���ɫh�|���L�w�g��J�s�����M��";
                btnData4.LargeImage = imgSrc4;
            }

            PushButtonData btnData5 = new PushButtonData(
"MyButton_ReNum",
"��ٮM��\n   ���s�s��   ",
Assembly.GetExecutingAssembly().Location,
"BeamCasing_ButtonCreate.ReUpdateCastNumber"
);
            {
                btnData5.ToolTip = "��ٮM�ޭ��s�s��";
                btnData5.LongDescription = "�ھڨC�h�Ӫ��}�f�ƶq�A���s�a�J�s��";
                btnData5.LargeImage = imgSrc5;
            }

            PushButtonData btnData6 = new PushButtonData(
"MyButton_CopyLinked",
"�ƻs�Ҧ�\n   �~�ѮM��   ",
Assembly.GetExecutingAssembly().Location,
"BeamCasing_ButtonCreate.CopyAllCast"
);
            {
                btnData6.ToolTip = "�ƻs�Ҧ��s���ҫ������M��";
                btnData6.LongDescription = "�ƻs�Ҧ��s���ҫ������M�ޡA�H��SEM�}�f�s����";
                btnData6.LargeImage = imgSrc6;
            }

            //��s��ٸ�T(��s&�]�w)
            SplitButtonData setUpButtonData = new SplitButtonData("CastSetUpButton", "��ٮM�ާ�s");
            SplitButton splitButton1 = panel.AddItem(setUpButtonData) as SplitButton;
            PushButton button2 = splitButton1.AddPushButton(btnData2);
            button2 = splitButton1.AddPushButton(btnData3);

            //�Ыج�ٮM��(ST&RC)
            PushButton button0 = panel.AddItem(btnData0) as PushButton;
            PushButton button = panel.AddItem(btnData) as PushButton;

            //splitButton1.AddPushButton(btnData2);
            //splitButton1.AddPushButton(btnData3);
            //PushButton button2 = panel.AddItem(btnData2) as PushButton;
            //PushButton button3 = panel.AddItem(btnData3) as PushButton;

            //�ƻs�Ҧ��M��
            PushButton button6 = panel2.AddItem(btnData6) as PushButton;

            //��ٮM�޽s��(�s��&���s)
            SplitButtonData setNumButtonData = new SplitButtonData("CastSetNumButton", "��ٮM�޽s��");
            SplitButton splitButton2 = panel2.AddItem(setNumButtonData) as SplitButton;
            PushButton button4 =splitButton2.AddPushButton(btnData4);
            button4 = splitButton2.AddPushButton(btnData5);
            //splitButton2.AddPushButton(btnData5);

            //PushButton button4 = panel.AddItem(btnData4) as PushButton;
            //PushButton button5 = panel.AddItem(btnData5) as PushButton;

            //pullDownButton�]�w��k
            //PulldownButtonData pulldownButtonData = new PulldownButtonData("MyButton_Num", "�M�޽s��");
            //pulldownButtonData.Image
            //PulldownButton pulldownGroup = panel.AddItem(pulldownButtonData) as PulldownButton;
            //PushButton button4= pulldownGroup.AddPushButton(btnData4) as PushButton;
            //PushButton button5 = pulldownGroup.AddPushButton(btnData5) as PushButton;


            //�w�]Enabled���ӴN��true�A���ίS�O�]�w
            button0.Enabled = true;
            button.Enabled = true;
            //splitButton1.Enabled = true;
            //splitButton2.Enabled = true;
            //button2.Enabled = true;
            //button4.Enabled = true;
            //splitButton1.Enabled = true;
            //splitButton2.Enabled = true;
            //button2.Enabled = true;
            //button3.Enabled = true;
            //button4.Enabled = true;
            //button5.Enabled = true;

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
