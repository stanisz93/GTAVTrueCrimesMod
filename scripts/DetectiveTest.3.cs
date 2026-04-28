using GTA;
using GTA.UI;
using System;
using System.Windows.Forms;

public class DetectiveTest : Script
{
    private bool firstTick = false;

    public DetectiveTest()
    {
        Tick += OnTick;
        KeyDown += OnKeyDown;
        Interval = 1000;
    }

    private void OnTick(object sender, EventArgs e)
    {
        if (!firstTick)
        {
            firstTick = true;
            GTA.UI.Screen.ShowSubtitle("DetectiveTest loaded!", 5000);
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F7)
        {
            GTA.UI.Screen.ShowSubtitle("F7 works!", 3000);
        }
    }
}