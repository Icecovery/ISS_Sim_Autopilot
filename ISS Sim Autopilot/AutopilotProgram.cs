using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using CefSharp;
using CefSharp.WinForms;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using System.ComponentModel.Design.Serialization;
using System.Timers;

namespace ISS_Sim_Autopilot
{
    public partial class Autopilot : Form
    {
        private const double RotationRateDelta = 0.1d;
        private const double TranslationRateXDelta = 0.06d;
        private const double TranslationRateYZDelta = 0.05d;
        private double updatesPerSec = 1d;
        private double deltaTime = 1d;
        private DateTime lastTickTime;
        private double lerpValue = 0.1d;

        #region data
        StringBuilder sb;
        private AutopilotPhase autopilotPhase;
        private string source;
        private double missionDuration;
        private double x, y, z;
        private double x_Rate, y_Rate, z_Rate;
        private double pitch_Error, yaw_Error, roll_Error;
        private double pitch_Rate, yaw_Rate, roll_Rate;
        private double range;
        private double rate;
        private string errorMessage;
        private string action;
        #endregion

        private void StartAutopilot()
        {
            sb = new StringBuilder();
            updatesPerSec = 1000d / timerAutopilot.Interval;
            deltaTime = timerAutopilot.Interval / 1000d;
            lerpValue = 0.9d;
            lastTickTime = DateTime.Now;
            missionDuration = 0;
            autopilotPhase = (AutopilotPhase)0;
            timerAutopilot.Enabled = true;
        }
        private void EndAutopilot()
        {
            timerAutopilot.Enabled = false;
        }

        private void Match(double error, double rate, double delta, Direction posAction, Direction negAction, double sensitivity, double limit = double.PositiveInfinity, bool doLock = false)
        {
            //if (Math.Abs(error) < tolerance)
            //   return;
            if (Math.Abs(error) < 0.2d)
            {
                sb.Append("Locked").AppendLine();
                if (doLock)
                {
                    if (rate <= -delta)
                        Move(posAction);
                    else if (rate >= delta)
                        Move(negAction);
                    else
                        Move(Direction.None);
                }
                else
                {
                    if (Math.Abs(rate) > delta)
                    {
                        if (rate < 0)
                            Move(posAction);
                        else if (rate > 0)
                            Move(negAction);
                        else
                            Move(Direction.None);
                    }
                }
            }
            else
            {
                if ((error >= 0 && rate >= 0) || (error < 0 && rate < 0))
                {
                    double timeToZero = Math.Abs((error - rate * deltaTime) / (rate + double.Epsilon));
                    double maxActionPerSec = delta * updatesPerSec / sensitivity;
                    double maxActionBeforeZero = maxActionPerSec * timeToZero;

                    sb.Append("T-: ").AppendFormat("{0:N3}", timeToZero).AppendLine();

                    if (Math.Abs(rate) > maxActionBeforeZero)
                    {
                        if (error < 0)
                            Move(posAction);
                        else
                            Move(negAction);
                    }
                    else
                    {
                        if (error < 0)
                            Move(negAction);
                        else
                            Move(posAction);
                    }
                }
                else
                {
                    if (rate > 0 && error < 0)
                    {
                        sb.Append("Reversing").AppendLine();
                        Move(negAction);
                    }
                    else if (rate < 0 && error > 0)
                    {
                        sb.Append("Reversing").AppendLine();
                        Move(posAction);
                    }
                    else
                    {
                        sb.Append("Good").AppendLine();
                        Move(Direction.None);
                    }
                }
            }
        }

        private void AutopilotTick()
        {
            deltaTime = (DateTime.Now - lastTickTime).TotalSeconds;
            lastTickTime = DateTime.Now;
            updatesPerSec = 1d / deltaTime;

            _ = GetData();
            UpdateTextBox();

            switch (autopilotPhase)
            {
                case AutopilotPhase.Waiting:
                    missionDuration = 0;
                    if (!double.IsNaN(x))
                        autopilotPhase++;

                    Move(Direction.None);
                    break;
                case AutopilotPhase.FixingRoll:
                    if (double.IsNaN(x))
                        autopilotPhase--;
                    Match(roll_Error, roll_Rate, RotationRateDelta, Direction.RollRight, Direction.RollLeft, 2d);
                    if (Math.Abs(roll_Error) < 0.2d)
                    {
                        autopilotPhase += 2;
                        Move(Direction.PitchUp);
                        Move(Direction.YawLeft);
                    }    
                    break;
                case AutopilotPhase.FixingDirection:
                    Match(pitch_Error, pitch_Rate, RotationRateDelta, Direction.PitchDown, Direction.PitchUp, 4d);
                    Match(yaw_Error, yaw_Rate, RotationRateDelta, Direction.YawRight, Direction.YawLeft, 4d);
                    Match(roll_Error, roll_Rate, RotationRateDelta, Direction.RollRight, Direction.RollLeft, 4d);

                    if (Math.Abs(pitch_Error) < 0.2d || Math.Abs(yaw_Error) < 0.2d)
                        autopilotPhase++;
                    break;
                case AutopilotPhase.Approaching:
                    Match(x - 10d, x_Rate, TranslationRateXDelta, Direction.xNeg, Direction.xPos, 1d);
                    Match(y, y_Rate, TranslationRateYZDelta, Direction.yNeg, Direction.yPos, 2d);
                    Match(z, z_Rate, TranslationRateYZDelta, Direction.zNeg, Direction.zPos, 2d);
                    Match(pitch_Error, pitch_Rate, RotationRateDelta, Direction.PitchDown, Direction.PitchUp, 4d);
                    Match(yaw_Error, yaw_Rate, RotationRateDelta, Direction.YawRight, Direction.YawLeft, 4d);
                    Match(roll_Error, roll_Rate, RotationRateDelta, Direction.RollRight, Direction.RollLeft, 4d);
                    if (Math.Abs(x - 10d) <= 0.5d && Math.Abs(y) < 0.5d && Math.Abs(z) < 0.5d)
                    {
                        autopilotPhase++;
                    }
                    break;
                case AutopilotPhase.Stop:
                    Match(x - 5d, x_Rate, TranslationRateXDelta, Direction.xNeg, Direction.xPos, 2d);
                    Match(y, y_Rate, TranslationRateYZDelta, Direction.yNeg, Direction.yPos, 2d);
                    Match(z, z_Rate, TranslationRateYZDelta, Direction.zNeg, Direction.zPos, 2d);
                    Match(pitch_Error, pitch_Rate, RotationRateDelta, Direction.PitchDown, Direction.PitchUp, 4d);
                    Match(yaw_Error, yaw_Rate, RotationRateDelta, Direction.YawRight, Direction.YawLeft, 4d);
                    Match(roll_Error, roll_Rate, RotationRateDelta, Direction.RollRight, Direction.RollLeft, 4d);
                    if (Math.Abs(x - 5d) <= 3d && Math.Abs(y) <= 0.11d && Math.Abs(z) <= 0.11d)
                    {
                        autopilotPhase++;
                        timerAutopilot.Interval = 200;
                    }
                    break;
                case AutopilotPhase.Docking:
                    Match(x - 0.34d, x_Rate, TranslationRateXDelta, Direction.xNeg, Direction.xPos, 4d, 0.05d);
                    Match(y, y_Rate, TranslationRateYZDelta, Direction.yNeg, Direction.yPos, 2d, doLock: true);
                    Match(z, z_Rate, TranslationRateYZDelta, Direction.zNeg, Direction.zPos, 2d, doLock: true);
                    break;
                case AutopilotPhase.Docked:
                    timerAutopilot.Enabled = false;
                    EndAutopilot();
                    buttonStart.Text = "Initiate Autopilot";
                    break;
                default:
                    break;
            }
  
            richTextBox.Text = sb.ToString();
        }
        private new void Move(Direction direction)
        {
            switch (direction)
            {
                case Direction.xPos:
                    SendKeys.Send("q");
                    break;
                case Direction.xNeg:
                    SendKeys.Send("e");
                    break;
                case Direction.yPos:
                    SendKeys.Send("d");
                    break;
                case Direction.yNeg:
                    SendKeys.Send("a");
                    break;
                case Direction.zPos:
                    SendKeys.Send("w");
                    break;
                case Direction.zNeg:
                    SendKeys.Send("s");
                    break;
                case Direction.PitchUp:
                    SendKeys.Send("{UP}");
                    break;
                case Direction.PitchDown:
                    SendKeys.Send("{DOWN}");
                    break;
                case Direction.YawRight:
                    SendKeys.Send("{RIGHT}");
                    break;
                case Direction.YawLeft:
                    SendKeys.Send("{LEFT}");
                    break;
                case Direction.RollRight:
                    SendKeys.Send(".");
                    break;
                case Direction.RollLeft:
                    SendKeys.Send(",");
                    break;
                default:
                    break;
            }
            action = direction.ToString();
        }
        private async Task GetData()
        {
            try
            {
                if (autopilotPhase > (AutopilotPhase)0 && autopilotPhase < (AutopilotPhase)6)
                    missionDuration += deltaTime;
                source = await browser.GetBrowser().MainFrame.GetSourceAsync();

                HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();
                doc.LoadHtml(source);
                HtmlNode node = doc.DocumentNode;
                double tx = GetNumber(node, "//div[@id='x-range']", "m");
                double ty = GetNumber(node, "//div[@id='y-range']", "m");
                double tz = GetNumber(node, "//div[@id='z-range']", "m");
                x_Rate = Lerp(x_Rate, (x - tx) / deltaTime, lerpValue);
                y_Rate = Lerp(y_Rate, (y - ty) / deltaTime, lerpValue);
                z_Rate = Lerp(z_Rate, (z - tz) / deltaTime, lerpValue);
                x = Lerp(x, tx, lerpValue); ;
                y = Lerp(y, ty, lerpValue); ;
                z = Lerp(z, tz, lerpValue); ;
                pitch_Error = GetNumber(node, "//div[@id='pitch']/div[@class='error']", "°");
                pitch_Rate = GetNumber(node, "//div[@id='pitch']/div[@class='rate']", "°/s", "//div[@id='pitch']/div[@class='rate warning']", "//div[@id='pitch']/div[@class='rate caution']");
                yaw_Error = GetNumber(node, "//div[@id='yaw']/div[@class='error']", "°");
                yaw_Rate = GetNumber(node, "//div[@id='yaw']/div[@class='rate']", "°/s", "//div[@id='yaw']/div[@class='rate warning']", "//div[@id='yaw']/div[@class='rate caution']");
                roll_Error = GetNumber(node, "//div[@id='roll']/div[@class='error']", "°");
                roll_Rate = GetNumber(node, "//div[@id='roll']/div[@class='rate']", "°/s", "//div[@id='roll']/div[@class='rate warning']", "//div[@id='roll']/div[@class='rate caution']");
                range = GetNumber(node, "//div[@id='range']/div[@class='rate']", "m");
                rate = GetNumber(node, "//div[@id='rate']/div[@class='rate']", "m/s", "//div[@id='rate']/div[@class='rate warning']", "//div[@id='rate']/div[@class='rate caution']");

                errorMessage = "None";
            }
            catch (Exception e)
            {
                errorMessage = e.Message;
            }    
        }

        private double Lerp(double first, double second, double by)
        {
            return first * (1d - by) + second * by;
        }

        private double GetNumber(HtmlNode node, string path, string replace, string back1 = "", string back2 = "")
        {
            try
            {
                if (node.SelectSingleNode(path) != null)
                    return double.Parse(node.SelectSingleNode(path).InnerText.Replace(replace, string.Empty));
                else if (node.SelectSingleNode(back1) != null)
                    return double.Parse(node.SelectSingleNode(back1).InnerText.Replace(replace, string.Empty));
                else if (node.SelectSingleNode(back2) != null)
                    return double.Parse(node.SelectSingleNode(back2).InnerText.Replace(replace, string.Empty));
                else
                    return double.NaN;
            }
            catch (Exception e)
            {
                errorMessage = e.Message;
                return double.NaN;
            }      
        }
        private void UpdateTextBox()
        {
            sb.Clear();
            sb.Append("Mission Duration: ").AppendFormat("{0:N2}", missionDuration).AppendLine();
            sb.AppendLine();
            sb.Append("Autopilot Phase: ").Append(autopilotPhase.ToString()).AppendLine();
            sb.AppendLine();
            sb.Append("x_Error: ").AppendFormat("{0:N2}", x).AppendLine();
            sb.Append("y_Error: ").AppendFormat("{0:N2}", y).AppendLine();
            sb.Append("z_Error: ").AppendFormat("{0:N2}", z).AppendLine();
            sb.Append("pitch_Error: ").Append(pitch_Error).AppendLine();
            sb.Append("yaw_Error: ").Append(yaw_Error).AppendLine();
            sb.Append("roll_Error: ").Append(roll_Error).AppendLine();
            sb.AppendLine();
            sb.Append("x_Rate: ").AppendFormat("{0:N2}", x_Rate).AppendLine();
            sb.Append("y_Rate: ").AppendFormat("{0:N2}", y_Rate).AppendLine();
            sb.Append("z_Rate: ").AppendFormat("{0:N2}", z_Rate).AppendLine();
            sb.Append("pitch_Rate: ").Append(pitch_Rate).AppendLine();
            sb.Append("yaw_Rate: ").Append(yaw_Rate).AppendLine();
            sb.Append("roll_Rate: ").Append(roll_Rate).AppendLine();
            sb.Append("range: ").Append(range).AppendLine();
            sb.Append("rate: ").Append(rate).AppendLine();
            sb.AppendLine();
            sb.Append("Error Message: ").Append(errorMessage).AppendLine();
            sb.AppendLine();
            sb.Append("Action: ").Append(action).AppendLine();
            sb.AppendLine();
        }
    }
}
