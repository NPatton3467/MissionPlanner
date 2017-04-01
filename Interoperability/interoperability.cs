﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Drawing;
using System.Windows.Forms;
using System.Xml;
using GMap.NET;
using GMap.NET.MapProviders;
using GMap.NET.WindowsForms;
using MissionPlanner;
using MissionPlanner.Utilities;
using MissionPlanner.GCSViews;

using System.Speech.Synthesis;
using System.Speech.Recognition;

using MissionPlanner.Plugin;

using Interoperability_GUI_Forms;

// Davis was here
// One or both of these is for HTTP requests. I forget which one
using System.Net;
using System.Net.Http;

//For spline interpolation
using Spline;

//For javascript serializer
using System.Web.Script.Serialization;

using System.IO; // For logging

using System.Threading; // Trololo
using System.Diagnostics; // For stopwatch


/* NOTES TO SELF
 * 
 * 1. All members inherited from abstracts need an "override" tag added in front
 * 2. Basically everything -inherited from abstracts- is temporary right now (eg. "return true")
 * 3. It seems that WebRequest is the right thing to use (ie. it isn't deprecated) THIS IS VERY FALSE
 * 
 */

namespace interoperability
{
    public class Interoperability : Plugin
    {
        //Default credentials if credentials file does not exist
        private string address = "http://192.168.56.101";
        private string username = "testuser";
        private string password = "testpass";

        private string dist_units = "Metres";
        private string airspd_units = "Metres per Second";
        private string geo_cords = "DD.DDDDDD";

        //SDA Simulator Values
        private double sim_lat = 0;
        private double sim_lng = 0;
        private float sim_alt = 0;
        private float sim_yaw = 0;
        private int sim_next_wp = 0;

        private static Mutex Interoperability_Action_Mutex = new Mutex();

        private Thread Telemetry_Thread;
        private Thread Obstacle_SDA_Thread;
        private Thread Mission_Thread;
        private Thread Map_Control_Thread;
        private Thread Callout_Thread;
        private Thread SDA_Plane_Simulator_Thread;
        private Thread SDA_Avoidance_Algorithm_Thread;

        private bool Telemetry_Thread_shouldStop = true;        //Used to start/stop the telemtry thread
        private bool Obstacle_SDA_Thread_shouldStop = true;     //Used to start/stop the SDA thread
        private bool Mission_Thread_shouldStop = true;          //Used to start/stop the misison thread
        private bool Map_Control_Thread_shouldStop = true;      //Used to start/stop the map control thread
        private bool Callout_Thread_shouldStop = true;          //Used to start/stop the callout thread
        private bool SDA_Plane_Simulator_Thread_shouldStop = true;
        private bool SDA_Avoidance_Algorithm_Thread_shouldStop = true;

        private int ImportantCounter = 0;
        private long ImporantTimeCount = 0;
        private Stopwatch ImportantTimer = new Stopwatch();

        bool Obstacles_Downloaded = false;                  //Used to tell the map control thread we can access obstaclesList 
        bool resetUploadStats = false;                      //Used to reset telemetry upload stats
        bool resetFlightTimer = false;                      //Used to reset the flight timer
        bool usePlaneSimulator = false;                     //Used for the plane simulator

        Obstacles obstaclesList;                            //Instance that holds all SDA Obstacles 
        public Interoperability_Settings Settings;          //Instance that holds all Interoperability Settings

        public List<Mission> Mission_List { get; set; }   //Holds a list of all missions from interoperability + Server
        public Mission Current_Mission { get; set; }      //The current mission open in the program  

        private static Interoperability Instance = null;


        public static Mutex Interoperability_Mutex = new Mutex();

        //Instantiate windows forms
        Interoperability_GUI_Main Interoperability_GUI;

        override public string Name
        {
            get { return ("Interoperability"); }
        }
        override public string Version
        {
            get { return ("0.7.3"); }
        }
        override public string Author
        {
            get { return ("Jesse, Davis, Oliver"); }
        }


        /// <summary>
        /// Run First, checking plugin
        /// </summary>
        /// <returns></returns>
        override public bool Init()
        {
            Instance = this;
            // System.Windows.Forms.MessageBox.Show("Pong");
            Console.Write("* * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * *\n"
                + "*                                   UTAT UAV                                  *\n"
                + "*                            Interoperability 0.3.1                           *\n"
                + "* * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * *\n");

            //Set up settings object, and load from xml file
            Settings = new Interoperability_Settings();
            Settings.Load();
            getSettings();

            //Instantiate all threads, but do not start
            Telemetry_Thread = new Thread(new ThreadStart(this.Telemetry_Upload));
            Obstacle_SDA_Thread = new Thread(new ThreadStart(this.Obstacle_SDA));
            Mission_Thread = new Thread(new ThreadStart(this.Mission_Download));
            Callout_Thread = new Thread(new ThreadStart(this.Callouts));
            SDA_Plane_Simulator_Thread = new Thread(new ThreadStart(this.SDA_Plane_Simulator));
            SDA_Avoidance_Algorithm_Thread = new Thread(new ThreadStart(this.SDA_Avoidance_Algorithm));
            Map_Control_Thread = new Thread(new ThreadStart(this.Map_Control));

            //Instantiate Mission_List
            Mission_List = new List<Mission>();
            Current_Mission = new Mission();

            // Start interface
            Interoperability_GUI = new Interoperability_GUI_Forms.Interoperability_GUI_Main(this.interoperabilityAction, Settings);
            if (Convert.ToBoolean(Settings["showInteroperability_GUI"]) == true)
            {
                Interoperability_GUI.Show();
                //Start map thread
                Map_Control_Thread = new Thread(new ThreadStart(this.Map_Control));
                Map_Control_Thread_shouldStop = false;
                Map_Control_Thread.Start();
            }

            loopratehz = 0.25F;

            //Add item to flight data context menu 
            Host.FDMenuMap.Items.Add(Interoperability_GUI.getContextMenu());
            Host.MainForm.MainMenu.Items.Insert(2, Interoperability_GUI.getMenuStrip());

            // MyView.AddScreen(new MainSwitcher.Screen("FlightData", FlightData, true));
            //Start Important Timer
            ImportantTimer.Start();

            Console.WriteLine("End of init()");

            return (true);
        }

        /// <summary>
        /// Returns the current interoperability instance
        /// </summary>
        /// <returns></returns>
        public static Interoperability getinstance()
        {
            return Instance;
        }

        public enum Interop_Action
        {
            Telemtry_Thread_Start,
            Telemtry_Thread_Stop,
            Obstacle_SDA_Thread_Start,
            Obstacle_SDA_Thread_Stop,
            Mission_Download_Run,
            Telemetry_Thread_Reset_Stats,
            Restart_Threads_Settings,
            Stop_All_Threads_Quit,
            Show_Interop_GUI,
            Easter_Egg_Action,
            Callout_Thread_Start,
            Callout_Thread_Stop,
            Map_Thread_Restart_Flight_Timer,
            Clear_Cur_Mission,
            SDA_Plane_Simulator_Thread_Start,
            SDA_Plane_Simulator_Thread_Stop,
            SDA_Avoidance_Algorithm_Thread_Start,
            SDA_Avoidance_Algorithm_Thread_Stop
        }

        /// <summary>
        /// Allows other functions to control the interoperability threads.
        /// </summary>
        /// <param name="action"></param>
        public void interoperabilityAction(Interop_Action action)
        {
            Interoperability_Action_Mutex.WaitOne();
            switch (action)
            {
                //Start Telemetry_Upload Thread
                case Interop_Action.Telemtry_Thread_Start:
                    Stop_Thread(ref Telemetry_Thread, ref Telemetry_Thread_shouldStop);
                    Telemetry_Thread = new Thread(new ThreadStart(this.Telemetry_Upload));
                    Telemetry_Thread_shouldStop = false;
                    Telemetry_Thread.Start();
                    break;

                //Stop telemtry upload thread
                case Interop_Action.Telemtry_Thread_Stop:
                    Stop_Thread(ref Telemetry_Thread, ref Telemetry_Thread_shouldStop);
                    break;

                //Start Obstacle_SDA Thread
                case Interop_Action.Obstacle_SDA_Thread_Start:
                    Stop_Thread(ref Obstacle_SDA_Thread, ref Obstacle_SDA_Thread_shouldStop);
                    Obstacle_SDA_Thread = new Thread(new ThreadStart(this.Obstacle_SDA));
                    Obstacle_SDA_Thread_shouldStop = false;
                    Obstacle_SDA_Thread.Start();
                    break;

                //Stop Obstacle_SDA Thread
                case Interop_Action.Obstacle_SDA_Thread_Stop:
                    Stop_Thread(ref Obstacle_SDA_Thread, ref Obstacle_SDA_Thread_shouldStop);
                    break;

                //Run mission download thread
                case Interop_Action.Mission_Download_Run:
                    Stop_Thread(ref Mission_Thread, ref Mission_Thread_shouldStop);
                    Mission_Thread = new Thread(new ThreadStart(this.Mission_Download));
                    Mission_Thread_shouldStop = false;
                    Mission_Thread.Start();
                    break;

                //Reset Telemetry Upload Rate Stats
                case Interop_Action.Telemetry_Thread_Reset_Stats:
                    resetUploadStats = true;
                    if (Telemetry_Thread.IsAlive == false)
                    {
                        Interoperability_GUI.setAvgTelUploadText("0Hz");
                        Interoperability_GUI.setUniqueTelUploadText("0Hz");
                        Interoperability_GUI.setTotalTelemUpload(0);
                    }
                    break;

                //Restart all running threads that rely on server credentials or unit settings
                case Interop_Action.Restart_Threads_Settings:
                    getSettings();
                    //No need to reset the map control thread
                    if (Map_Control_Thread.IsAlive == false)
                    {
                        Stop_Thread(ref Map_Control_Thread, ref Map_Control_Thread_shouldStop);
                        Map_Control_Thread = new Thread(new ThreadStart(this.Map_Control));
                        Map_Control_Thread_shouldStop = false;
                        Map_Control_Thread.Start();
                    }
                    //If GUI format is not AUVSI, disable all AUVSI Threads
                    if (Settings["gui_format"] != "AUVSI")
                    {
                        Stop_Thread(ref Telemetry_Thread, ref Telemetry_Thread_shouldStop);
                        Stop_Thread(ref Obstacle_SDA_Thread, ref Obstacle_SDA_Thread_shouldStop);
                        Stop_Thread(ref Mission_Thread, ref Mission_Thread_shouldStop);
                        Stop_Thread(ref SDA_Avoidance_Algorithm_Thread, ref SDA_Avoidance_Algorithm_Thread_shouldStop);
                        Stop_Thread(ref SDA_Plane_Simulator_Thread, ref SDA_Plane_Simulator_Thread_shouldStop);
                    }
                    else
                    {
                        if (Telemetry_Thread.IsAlive)
                        {
                            Stop_Thread(ref Telemetry_Thread, ref Telemetry_Thread_shouldStop);
                            Telemetry_Thread = new Thread(new ThreadStart(this.Telemetry_Upload));
                            Telemetry_Thread_shouldStop = false;
                            Telemetry_Thread.Start();
                        }
                        if (Obstacle_SDA_Thread.IsAlive)
                        {
                            Stop_Thread(ref Obstacle_SDA_Thread, ref Obstacle_SDA_Thread_shouldStop);
                            Obstacle_SDA_Thread = new Thread(new ThreadStart(this.Obstacle_SDA));
                            Obstacle_SDA_Thread_shouldStop = false;
                            Obstacle_SDA_Thread.Start();
                        }
                        if (Mission_Thread.IsAlive)
                        {
                            Stop_Thread(ref Mission_Thread, ref Mission_Thread_shouldStop);
                            Mission_Thread = new Thread(new ThreadStart(this.Mission_Download));
                            Mission_Thread_shouldStop = false;
                            Mission_Thread.Start();
                        }
                    }

                    break;

                //Stop all threads, when loop until all threads are done
                case Interop_Action.Stop_All_Threads_Quit:

                    Telemetry_Thread_shouldStop = true;
                    Mission_Thread_shouldStop = true;
                    Obstacle_SDA_Thread_shouldStop = true;
                    Map_Control_Thread_shouldStop = true;
                    Callout_Thread_shouldStop = true;
                    SDA_Plane_Simulator_Thread_shouldStop = true;
                    SDA_Avoidance_Algorithm_Thread_shouldStop = true;

                    Stopwatch t = new Stopwatch();
                    t.Start();
                    while (Mission_Thread.IsAlive || Obstacle_SDA_Thread.IsAlive || Telemetry_Thread.IsAlive || Map_Control_Thread.IsAlive ||
                        Callout_Thread.IsAlive || SDA_Plane_Simulator_Thread.IsAlive || SDA_Avoidance_Algorithm_Thread.IsAlive)
                    {
                        //If all threads haven't quit in 1 seconds, force quit
                        if (t.ElapsedMilliseconds > 1000)
                        {
                            Telemetry_Thread.Abort();
                            Mission_Thread.Abort();
                            Obstacle_SDA_Thread.Abort();
                            Map_Control_Thread.Abort();
                            Callout_Thread.Abort();
                            SDA_Avoidance_Algorithm_Thread.Abort();
                            SDA_Plane_Simulator_Thread.Abort();
                            break;
                        }
                        //Wait until all threads have stopped
                    }
                    break;

                //Show the interoperability control panel
                case Interop_Action.Show_Interop_GUI:
                    if (!Interoperability_GUI.isOpened)
                    {
                        Interoperability_GUI = new Interoperability_GUI_Forms.Interoperability_GUI_Main(this.interoperabilityAction, Settings);
                        Interoperability_GUI.Show();
                        //Start map thread
                        Stop_Thread(ref Map_Control_Thread, ref Map_Control_Thread_shouldStop);
                        Map_Control_Thread = new Thread(new ThreadStart(this.Map_Control));
                        Map_Control_Thread_shouldStop = false;
                        Map_Control_Thread.Start();
                    }
                    else
                    {
                        Interoperability_GUI.BringToFront();
                        if (Interoperability_GUI.WindowState == FormWindowState.Minimized)
                        {
                            Interoperability_GUI.WindowState = FormWindowState.Normal;
                        }
                    }
                    break;

                //Easter egg
                case Interop_Action.Easter_Egg_Action:
                    if (ImportantTimer.ElapsedMilliseconds - ImporantTimeCount > 100)
                    {
                        switch (ImportantCounter)
                        {
                            case 0:
                                Host.MainForm.MainMenu.Items[2].Image = Properties.Resources.Interop_Icon_Oliver;
                                break;
                            case 1:
                                Host.MainForm.MainMenu.Items[2].Image = Properties.Resources.Interop_Icon_Yih_Tang;
                                break;
                            case 2:
                                Host.MainForm.MainMenu.Items[2].Image = Properties.Resources.Interop_Icon_Erik;
                                break;
                            case 3:
                                Host.MainForm.MainMenu.Items[2].Image = Properties.Resources.Interop_Icon_Jesse;
                                break;
                            case 4:
                                Host.MainForm.MainMenu.Items[2].Image = Properties.Resources.Interop_Icon_Rikky;
                                break;
                            default:
                                Host.MainForm.MainMenu.Items[2].Image = Properties.Resources.Interop_Icon;
                                ImportantCounter = -1;
                                break;
                        }
                        ImporantTimeCount = ImportantTimer.ElapsedMilliseconds;
                        ImportantCounter++;
                    }
                    break;

                //Start callout thread
                case Interop_Action.Callout_Thread_Start:
                    Stop_Thread(ref Callout_Thread, ref Callout_Thread_shouldStop);
                    Callout_Thread = new Thread(new ThreadStart(this.Callouts));
                    Callout_Thread_shouldStop = false;
                    Callout_Thread.Start();
                    break;

                //Stop callout thread
                case Interop_Action.Callout_Thread_Stop:
                    Stop_Thread(ref Callout_Thread, ref Callout_Thread_shouldStop);
                    break;

                //Reset Flight Timer
                case Interop_Action.Map_Thread_Restart_Flight_Timer:
                    resetFlightTimer = true;
                    break;

                //Clear current mission
                case Interop_Action.Clear_Cur_Mission:
                    Current_Mission = new Mission();
                    Current_Mission.name = "TEST RESET";
                    break;

                //Start simulator
                case Interop_Action.SDA_Plane_Simulator_Thread_Start:
                    Stop_Thread(ref SDA_Plane_Simulator_Thread, ref SDA_Plane_Simulator_Thread_shouldStop);
                    SDA_Plane_Simulator_Thread = new Thread(new ThreadStart(this.SDA_Plane_Simulator));
                    SDA_Plane_Simulator_Thread_shouldStop = false;
                    usePlaneSimulator = true;
                    SDA_Plane_Simulator_Thread.Start();
                    break;

                //Stop simulator
                case Interop_Action.SDA_Plane_Simulator_Thread_Stop:
                    usePlaneSimulator = false;
                    Stop_Thread(ref SDA_Plane_Simulator_Thread, ref SDA_Plane_Simulator_Thread_shouldStop);
                    break;

                //Start or stop SDA avoidance algorithm
                case Interop_Action.SDA_Avoidance_Algorithm_Thread_Start:
                    Stop_Thread(ref SDA_Avoidance_Algorithm_Thread, ref SDA_Avoidance_Algorithm_Thread_shouldStop);
                    SDA_Avoidance_Algorithm_Thread = new Thread(new ThreadStart(this.SDA_Avoidance_Algorithm));
                    SDA_Avoidance_Algorithm_Thread_shouldStop = false;
                    SDA_Avoidance_Algorithm_Thread.Start();
                    break;

                case Interop_Action.SDA_Avoidance_Algorithm_Thread_Stop:
                    Stop_Thread(ref SDA_Avoidance_Algorithm_Thread, ref SDA_Avoidance_Algorithm_Thread_shouldStop);
                    break;

                default:
                    break;
            }
            Interoperability_Action_Mutex.ReleaseMutex();
            return;
        }

        private void Stop_Thread(ref Thread _thread, ref bool shouldStop_Variable)
        {
            if(_thread.IsAlive)
            {
                shouldStop_Variable = true;
                _thread.Join(50);
            }

            if (_thread.IsAlive)
            {
                _thread.Abort();
            }
            return;
        }


        public void test_function()
        {

            //Doesn't seem to work. Need to modify FlightData.cs or ConfigPlanner.cs
            MissionPlanner.Utilities.Settings test = this.Host.config;
            test["CMB_rateattitude"] = "1";
            test.Save();

            //this.Host.FDMenuMap.
        }


        /// <summary>
        /// Loads all the settings from the Interoperability_Config.xml
        /// </summary>
        public void getSettings()
        {
            if (Settings.ContainsKey("dist_units") && Settings.ContainsKey("airspd_units") && Settings.ContainsKey("geo_cords"))
            {
                dist_units = Settings["dist_units"];
                airspd_units = Settings["airspd_units"];
                geo_cords = Settings["geo_cords"];
            }
            else
            {
                Settings["dist_units"] = dist_units;
                Settings["airspd_units"] = airspd_units;
                Settings["geo_cords"] = geo_cords;
            }
            if (Settings.ContainsKey("address") && Settings.ContainsKey("username") && Settings.ContainsKey("password"))
            {
                address = Settings["address"];
                username = Settings["username"];
                password = Settings["password"];
            }
            else
            {
                Settings["address"] = address;
                Settings["username"] = username;
                Settings["password"] = password;
            }
            if (!Settings.ContainsKey("showInteroperability_GUI"))
            {
                Settings["showInteroperability_GUI"] = false.ToString();
            }
            Settings.Save();
        }

        /// <summary>
        /// Runs in a loop, uploading telemtry data to the interoperability server
        /// </summary>
        public async void Telemetry_Upload()
        {
            Console.WriteLine("Telemetry_Upload Thread Started");
            Stopwatch t = new Stopwatch();
            t.Start();

            int count = 0;
            CookieContainer cookies = new CookieContainer();

            try
            {
                using (var client = new HttpClient())
                {

                    TimeSpan timeout = new TimeSpan(0, 0, 0, 1);
                    client.Timeout = timeout;

                    client.BaseAddress = new Uri(address); // This seems to change every time

                    // Log in.
                    Console.WriteLine("---INITIAL LOGIN---");
                    var v = new Dictionary<string, string>();
                    v.Add("username", username);
                    v.Add("password", password);
                    var auth = new FormUrlEncodedContent(v);
                    //Get authentication cookie. Cookie is automatically sent after being sent
                    HttpResponseMessage resp = await client.PostAsync("/api/login", auth);
                    Console.WriteLine("Login POST result: " + resp.Content.ReadAsStringAsync().Result);
                    Console.WriteLine("---LOGIN FINISHED---");

                    if (!resp.IsSuccessStatusCode)
                    {
                        Console.WriteLine("Invalid Credentials");
                        Interoperability_GUI.setAvgTelUploadText("Error, Invalid Credentials.");
                        Interoperability_GUI.setUniqueTelUploadText("Error, Invalid Credentials");
                        Interoperability_GUI.setTelemResp(resp.Content.ReadAsStringAsync().Result);
                        Interoperability_GUI.Telem_Start_Stop_Button_Off();
                        //interoperabilityAction(Interop_Action.Telemtry_Thread_Stop);
                        return;

                    }
                    else
                    {
                        Console.WriteLine("Credentials Valid");
                        //interoperabilityAction(Interop_Action.Telemtry_Thread_Stop);
                    }

                    CurrentState csl = this.Host.cs;
                    double lat = csl.lat, lng = csl.lng, alt = csl.altasl, yaw = csl.yaw;
                    double oldlat = 0, oldlng = 0, oldalt = 0, oldyaw = 0;
                    int uniquedata_count = 0;
                    double averagedata_count = 0;

                    while (Telemetry_Thread_shouldStop == false)
                    {
                        //Doesn't work, need another way to do this
                        //If person sets speed to 0, then GUI crashes 
                        if (Interoperability_GUI.getTelemPollRate() != 0)
                        {
                            csl = this.Host.cs;
                            lat = csl.lat;
                            lng = csl.lng;
                            alt = csl.altasl;
                            yaw = csl.yaw;
                            if (lat != oldlat || lng != oldlng || alt != oldalt || yaw != oldyaw)
                            {
                                uniquedata_count++;
                                averagedata_count++;
                                oldlat = csl.lat;
                                oldlng = csl.lng;
                                oldalt = csl.altasl;
                                oldyaw = csl.yaw;
                            }
                            if (count % Interoperability_GUI.getTelemPollRate() == 0)
                            {
                                Interoperability_GUI.setAvgTelUploadText((averagedata_count / (count / Interoperability_GUI.getTelemPollRate())) + "Hz");
                                Interoperability_GUI.setUniqueTelUploadText(uniquedata_count + "Hz");
                                uniquedata_count = 0;
                            }
                            if (resetUploadStats)
                            {
                                uniquedata_count = 0;
                                averagedata_count = 0;
                                count = 0;
                                resetUploadStats = false;
                            }


                            t.Restart();

                            var telemData = new Dictionary<string, string>();

                            CurrentState cs = this.Host.cs;

                            telemData.Add("latitude", lat.ToString("F10"));
                            telemData.Add("longitude", lng.ToString("F10"));
                            telemData.Add("altitude_msl", alt.ToString("F10"));
                            telemData.Add("uas_heading", yaw.ToString("F10"));
                            //Console.WriteLine("Latitude: " + lat + "\nLongitude: " + lng + "\nAltitude_MSL: " + alt + "\nHeading: " + yaw);

                            var telem = new FormUrlEncodedContent(telemData);
                            HttpResponseMessage telemresp = await client.PostAsync("/api/telemetry", telem);
                            Console.WriteLine("Server_info GET result: " + telemresp.Content.ReadAsStringAsync().Result);
                            Interoperability_GUI.setTelemResp(telemresp.Content.ReadAsStringAsync().Result);
                            count++;
                            Interoperability_GUI.setTotalTelemUpload(count);
                        }
                        Thread.Sleep(1000 / Interoperability_GUI.getTelemPollRate());
                    }
                }
            }

            //If this exception is thrown, then the thread will end soon after. Have no way to restart manually unless I get the loop working
            catch//(HttpRequestException)
            {
                //<h1>403 Forbidden</h1> 
                Interoperability_GUI.setAvgTelUploadText("Error, Unable to Connect to Server");
                Interoperability_GUI.setUniqueTelUploadText("Error, Unable to Connect to Server");
                Interoperability_GUI.setTelemResp("Error, Unable to Connect to Server");
                Interoperability_GUI.Telem_Start_Stop_Button_Off();
                Console.WriteLine("Error, exception thrown in telemtry upload thread");
            }
            Console.WriteLine("Telemetry_Upload Thread Stopped");
            Interoperability_GUI.Telem_Start_Stop_Button_Off();
        }


        public void addImage()
        {

        }

        /// <summary>
        /// Runs in a loop, downloading current obstacle locaitons from the server 
        /// </summary>
        public async void Obstacle_SDA()
        {
            Console.WriteLine("Obstacle_SDA Thread Started");
            CookieContainer cookies = new CookieContainer();

            try
            {
                using (var client = new HttpClient())
                {
                    TimeSpan timeout = new TimeSpan(0, 0, 0, 1);
                    client.Timeout = timeout;
                    client.BaseAddress = new Uri(address); // This seems to change every time

                    // Log in.
                    Console.WriteLine("---INITIAL LOGIN---");
                    var v = new Dictionary<string, string>();
                    v.Add("username", username);
                    v.Add("password", password);
                    var auth = new FormUrlEncodedContent(v);
                    HttpResponseMessage resp = await client.PostAsync("/api/login", auth);
                    Console.WriteLine("Login POST result: " + resp.Content.ReadAsStringAsync().Result);


                    if (!resp.IsSuccessStatusCode)
                    {
                        Console.WriteLine("Invalid Credentials");
                        Interoperability_GUI.setSDAResp(resp.Content.ReadAsStringAsync().Result);
                        Interoperability_GUI.SetSDAStart_StopButton_Off();
                        //interoperabilityAction(Interop_Action.Obstacle_SDA_Thread_Stop);
                        return;
                    }
                    else
                    {
                        Console.WriteLine("Credentials Valid");
                        Interoperability_GUI.setSDAResp(resp.Content.ReadAsStringAsync().Result);
                    }
                    Console.WriteLine("---LOGIN FINISHED---");

                    while (!Obstacle_SDA_Thread_shouldStop)
                    {
                        HttpResponseMessage SDAresp = await client.GetAsync("/api/obstacles");

                        obstaclesList = new JavaScriptSerializer().Deserialize<Obstacles>(SDAresp.Content.ReadAsStringAsync().Result);
                        Obstacles_Downloaded = true;
                        Interoperability_GUI.WriteObstacles(obstaclesList);

                        Thread.Sleep(1000 / Interoperability_GUI.getsdaPollRate());
                    }
                }
            }
            catch
            {
                Console.WriteLine("Error, exception thrown in Obstacle_SDA Thread");
                Interoperability_GUI.setSDAResp("Error, Unable to Connect to Server");
                Interoperability_GUI.SetSDAStart_StopButton_Off();

            }
            Interoperability_GUI.SetSDAStart_StopButton_Off();
            Console.WriteLine("Obstacle_SDA Thread Stopped");
        }


        private List<Waypoint> Simulator_Path;


        /// <summary>
        /// SDA algorithm. Runs periodically to avoid obstacles
        /// </summary>
        public void SDA_Avoidance_Algorithm()
        {

            while (SDA_Plane_Simulator_Thread_shouldStop == false)
            {
                /*Write your algorithm here
                You have access to: 
                    sim_lat         --Simulated latitude of the airplane
                    sim_lng         --Simulated longitude of the airplane
                    sim_alt         --Simulated altitude of the plane
                    sim_yaw         --Simulated heading of the plane (compass bearing)
                    sim_next_wp     --The next waypoint the plane will be moving towards 

                    obstaclesList   --Holds a list of all moving and stationary obstacles (updated constantly)
                        obstaclesList.moving_obstacles          --List of type 'Moving_Obstacle' 
                        obstaclesList.stationary_obstacles      --List of type 'Stationary_Obstacle'
                    Current_Mission.all_waypoints               --List of all waypoints the plane will be flying, type of 'Waypoint'
                    Interoperability_GUI.getPlaneSimulationAirspeed() --The plane's simulated airspeed (in meters per second)
                */




                Thread.Sleep(500);  //Change depending on how often you want to compute the algorithm
            }
        }

        private struct Vertex
        {
            public int x; //Vertex Coordinate
            public int y; //Vertex Coordinate
            public int p_x; //Parent X Coordinate
            public int p_y; //Parent Y Coordinate
            public double value; //Shortest distance from parent to this vertex 
        }

        /// <summary>
        /// Calculates the optimal path between two points given stationary obstacles and geofence
        /// </summary>
        public void Lazy_Theta(PointLatLng Start, PointLatLng End, Obstacles Mission_Obstacles, List<PointLatLng> Geofence)
        {
            /*Issues: 
                Doesn't check for illegal angles (>90 degrees)
                Doesn't check if we start in an occupied block? 

              ToDo: 
              Create a array or something to represent the map. Grid size to be determined. 
              Figure out how to mark a grid as occupied or blocked, so we don't go through that 
              Actually make the code
            */

            int gridsize = 10; //How far apart each vertex is in the x or y direction (in metres)

            double startX = MercatorProjection.lonToX(Start.Lng);
            double startY = MercatorProjection.latToY(Start.Lat);
            double endX = MercatorProjection.lonToX(End.Lng);
            double endY = MercatorProjection.latToY(End.Lat);

            double deltaX = endX - startX;
            double deltaY = endY - endY;

            List<List<Vertex>> Verticies = new List<List<Vertex>>();
            
            for(int i = 0; i < gridsize; i++)
            {
                List<Vertex> VertexList = new List<Vertex>();
                Vertex TempVertex = new Vertex;
                VertexList.Clear();
                
                for (int j = 0; j < gridsize; j++)
                {
                    TempVertex.x = 0;
                    TempVertex.y = 0;
                    TempVertex.p_x = 0;
                    TempVertex.p_y = 0;
                    TempVertex.value = 0;
                    VertexList.Add(TempVertex);
                }
            }

            List<Tuple<Tuple<int, int>, float>> open;
            List<Tuple<Tuple<int, int>, float>> closed;
        }

        public void SDA_Plane_Simulator()
        {
            //Save current waypoints stored
            List<Waypoint> Current_Waypoints = Current_Mission.all_waypoints;


            //Add fake waypoints 
            Current_Mission.all_waypoints.Clear();
            Current_Mission.all_waypoints.Add(new Waypoint(0, 38.144885, -76.428173));
            Current_Mission.all_waypoints.Add(new Waypoint(30, 38.146336, -76.428495));
            Current_Mission.all_waypoints.Add(new Waypoint(50, 38.147551, -76.429503));
            Current_Mission.all_waypoints.Add(new Waypoint(100, 38.148463, -76.430297));
            Current_Mission.all_waypoints.Add(new Waypoint(200, 38.149492, -76.431477));
            Current_Mission.all_waypoints.Add(new Waypoint(200, 38.150774, -76.432850));
            Current_Mission.all_waypoints.Add(new Waypoint(150, 38.150724, -76.433687));
            Current_Mission.all_waypoints.Add(new Waypoint(150, 38.150319, -76.434095));
            Current_Mission.all_waypoints.Add(new Waypoint(150, 38.149880, -76.433923));
            Current_Mission.all_waypoints.Add(new Waypoint(150, 38.149543, -76.433280));
            Current_Mission.all_waypoints.Add(new Waypoint(150, 38.149188, -76.432829));
            Current_Mission.all_waypoints.Add(new Waypoint(150, 38.146505, -76.430426));
            Current_Mission.all_waypoints.Add(new Waypoint(150, 38.145104, -76.429117));
            Current_Mission.all_waypoints.Add(new Waypoint(150, 38.142843, -76.427572));
            Current_Mission.all_waypoints.Add(new Waypoint(150, 38.142050, -76.427572));
            Current_Mission.all_waypoints.Add(new Waypoint(150, 38.141915, -76.428087));
            Current_Mission.all_waypoints.Add(new Waypoint(150, 38.144581, -76.430769));
            Current_Mission.all_waypoints.Add(new Waypoint(150, 38.144379, -76.431627));
            Current_Mission.all_waypoints.Add(new Waypoint(150, 38.141881, -76.429482));
            Current_Mission.all_waypoints.Add(new Waypoint(150, 38.141662, -76.430533));
            Current_Mission.all_waypoints.Add(new Waypoint(150, 38.144193, -76.432443));
            Current_Mission.all_waypoints.Add(new Waypoint(150, 38.143991, -76.433280));
            Current_Mission.all_waypoints.Add(new Waypoint(150, 38.141189, -76.431756));
            Current_Mission.all_waypoints.Add(new Waypoint(150, 38.140715, -76.431112));
            Current_Mission.all_waypoints.Add(new Waypoint(150, 38.141324, -76.426606));
            Current_Mission.all_waypoints.Add(new Waypoint(150, 38.142455, -76.424847));
            Current_Mission.all_waypoints.Add(new Waypoint(150, 38.145493, -76.426928));
            Current_Mission.all_waypoints.Add(new Waypoint(150, 38.145965, -76.426306));
            Current_Mission.all_waypoints.Add(new Waypoint(150, 38.142877, -76.423881));
            Current_Mission.all_waypoints.Add(new Waypoint(150, 38.143130, -76.422873));
            Current_Mission.all_waypoints.Add(new Waypoint(150, 38.146235, -76.425319));
            Current_Mission.all_waypoints.Add(new Waypoint(150, 38.146421, -76.424482));
            Current_Mission.all_waypoints.Add(new Waypoint(150, 38.143535, -76.422036));
            Current_Mission.all_waypoints.Add(new Waypoint(150, 38.144075, -76.421506));
            Current_Mission.all_waypoints.Add(new Waypoint(150, 38.146674, -76.423473));
            Current_Mission.all_waypoints.Add(new Waypoint(150, 38.143451, -76.426735));


            if (Current_Mission.all_waypoints.Count() < 3)
            {
                Console.WriteLine("Error, not enough waypoints to start simulator (Minimum 3)");
                return;
            }

            int target_waypoint = 1;
            int total_waypoints = Current_Mission.all_waypoints.Count();

            sim_lat = Current_Mission.all_waypoints[0].latitude;
            sim_lng = Current_Mission.all_waypoints[0].longitude;

            double ddist = 0;


            //Spline Algorithm
            //--------------------------------------------------------------------------------
            float[] x = new float[total_waypoints + 1]; //Plus one so the plane goes back to waypoint 0
            float[] y = new float[total_waypoints + 1];
            float[] xs;
            float[] ys;

            for (int i = 0; i < total_waypoints; i++)
            {
                x[i] = Current_Mission.all_waypoints[i].longitude;
                y[i] = Current_Mission.all_waypoints[i].latitude;
            }
            x[total_waypoints] = Current_Mission.all_waypoints[0].longitude;
            y[total_waypoints] = Current_Mission.all_waypoints[0].latitude;

            CubicSpline.FitParametric(x, y, 1000, out xs, out ys);

            Simulator_Path = new List<Waypoint>();
            for (int i = 0; i < xs.Count(); i++)
            {
                Simulator_Path.Add(new Waypoint(ys[i], xs[i]));
            }

            int[] altitude_array = new int[total_waypoints + 1];

            //Start calculating altitude spline... kinda
            int altitude_count = 0;
            for (int i = 0; i < xs.Count(); i++)
            {
                double dd;
                if (altitude_count < Current_Mission.all_waypoints.Count())
                {
                    double dx = MercatorProjection.lonToX(Current_Mission.all_waypoints[altitude_count].longitude - Simulator_Path[i].longitude);
                    double dy = MercatorProjection.latToY(Current_Mission.all_waypoints[altitude_count].latitude - Simulator_Path[i].latitude);
                    dd = Math.Sqrt(dx * dx + dy * dy);
                }
                else
                {
                    double dx = MercatorProjection.lonToX(Current_Mission.all_waypoints[0].longitude - Simulator_Path[i].longitude);
                    double dy = MercatorProjection.latToY(Current_Mission.all_waypoints[0].latitude - Simulator_Path[i].latitude);
                    dd = Math.Sqrt(dx * dx + dy * dy);
                }

                if (dd < 5)
                {
                    altitude_array[altitude_count] = i;
                    altitude_count++;
                }
            }

            for (int i = 0; i <= total_waypoints - 2; i++)
            {
                //Get number of indexes between start and end altitude
                int delta_index = altitude_array[i + 1] - altitude_array[i];
                double delta_altitude = (Current_Mission.all_waypoints[i + 1].altitude_msl - Current_Mission.all_waypoints[i].altitude_msl) / delta_index;
                for (int j = altitude_array[i]; j < altitude_array[i + 1]; j++)
                {
                    Simulator_Path[j].altitude_msl = (float)(Current_Mission.all_waypoints[i].altitude_msl + (j - altitude_array[i]) * delta_altitude);
                }

            }

            double Total_Distance = 0;
            //Get total distance of spline 
            for (int i = 0; i < Simulator_Path.Count() - 1; i++)
            {
                double dx = MercatorProjection.lonToX(Simulator_Path[i + 1].longitude) - MercatorProjection.lonToX(Simulator_Path[i].longitude);
                double dy = MercatorProjection.latToY(Simulator_Path[i + 1].latitude) - MercatorProjection.latToY(Simulator_Path[i].latitude);
                Total_Distance += Math.Sqrt(dx * dx + dy * dy);
            }

            double current_dist = 0;

            //-------------------------------------------------------------------------------------------
            //End Spline Algorithm

            while (SDA_Plane_Simulator_Thread_shouldStop == false)
            {
                //If using straight moving lines
                if (true)
                {
                    Simulator_Path.Clear();
                    foreach (Waypoint i in Current_Mission.all_waypoints)
                    {
                        Simulator_Path.Add(i);
                    }
                    
                    if (target_waypoint == total_waypoints)
                    {
                        sim_next_wp = 0;
                        target_waypoint = 0;
                    }
                    Console.WriteLine("Target Waypoint: " + target_waypoint.ToString());
                    // Gets an actual dx and dy in meters
                    double currX = MercatorProjection.lonToX(sim_lng);
                    double currY = MercatorProjection.latToY(sim_lat);
                    double dx = MercatorProjection.lonToX(Current_Mission.all_waypoints[target_waypoint].longitude) - currX;
                    double dy = MercatorProjection.latToY(Current_Mission.all_waypoints[target_waypoint].latitude) - currY;

                    double dalt = Current_Mission.all_waypoints[target_waypoint].altitude_msl - sim_alt;
                    //Since we're calling every 1/2 second, we move the plane AIRSPEED/2 meters per second
                    ddist = Interoperability_GUI.getPlaneSimulationAirspeed() / 10;

                    // Makes dx and dy the right length
                    double length = Math.Sqrt(dx * dx + dy * dy);
                    double conversionRatio = ddist / length;

                    if (ddist < length)
                    {
                        dx = dx * conversionRatio;
                        dy = dy * conversionRatio;
                        dalt = dalt * conversionRatio;
                    }

                    sim_yaw = (float)(90 - Math.Atan2(dy, dx) * 180 / Math.PI);
                    if (sim_yaw < 0)
                    {
                        sim_yaw += 360;
                    }

                    sim_lat = MercatorProjection.yToLat(currY + dy);
                    sim_lng = MercatorProjection.xToLon(currX + dx);
                    sim_alt += (float)dalt;


                    if (Math.Sqrt(Math.Pow(MercatorProjection.latToY(Current_Mission.all_waypoints[target_waypoint].latitude) - MercatorProjection.latToY(sim_lat), 2) +
                        Math.Pow(MercatorProjection.latToY(Current_Mission.all_waypoints[target_waypoint].longitude) - MercatorProjection.latToY(sim_lng), 2)) < 10)
                    {
                        target_waypoint++;
                        sim_next_wp++;
                    }
                }
                //Spline Interpolation
                //Not very accurate (assumes each point is the same distance apart, which it's not)
                else
                {
                    ddist = Interoperability_GUI.getPlaneSimulationAirspeed() / 10;
                    current_dist += ddist;

                    int spline_index = (int)Math.Ceiling(current_dist / (Total_Distance / Simulator_Path.Count()));

                    if (current_dist >= Total_Distance || spline_index >= Simulator_Path.Count() - 1)
                    {
                        current_dist = 0;
                        spline_index = 0;
                    }

                    sim_lat = Simulator_Path[spline_index].latitude;
                    sim_lng = Simulator_Path[spline_index].longitude;
                    sim_alt = Simulator_Path[spline_index].altitude_msl;

                    //Very stupid way of calculating target waypoint 
                    for (int i = 0; i <= total_waypoints; i++)
                    {
                        if (spline_index < altitude_array[i])
                        {
                            sim_next_wp = i;
                            break;
                        }
                        else if (spline_index > altitude_array[total_waypoints - 1])
                        {
                            sim_next_wp = 0;
                            break;
                        }
                    }


                    Console.WriteLine("Target Waypoint: " + sim_next_wp.ToString());

                    double dy = Simulator_Path[spline_index + 1].latitude - sim_lat;
                    double dx = Simulator_Path[spline_index + 1].longitude - sim_lng;

                    sim_yaw = (float)(90 - Math.Atan2(dy, dx) * 180 / Math.PI);
                    if (sim_yaw < 0)
                    {
                        sim_yaw += 360;
                    }


                }
                Thread.Sleep(100);
            }
            Current_Mission.all_waypoints = Current_Waypoints;
        }

        //Will be used to export to something. Is used to 
        public void ExportMapData(List<Mission> missionList)
        {
            string thing = new JavaScriptSerializer().Serialize(missionList);
            Console.WriteLine(thing);
        }


        public async void Mission_Download()
        {
            Console.WriteLine("Mission_Download Thread Started");
            Stopwatch t = new Stopwatch();
            t.Start();
            CookieContainer cookies = new CookieContainer();

            try
            {
                using (var client = new HttpClient())
                {
                    client.BaseAddress = new Uri(address); // This seems to change every time

                    // Log in.
                    var v = new Dictionary<string, string>();
                    v.Add("username", username);
                    v.Add("password", password);
                    var auth = new FormUrlEncodedContent(v);
                    HttpResponseMessage resp = await client.PostAsync("/api/login", auth);
                    if (!resp.IsSuccessStatusCode)
                    {
                        Mission_Thread_shouldStop = true;
                    }
                    else
                    {
                        Console.WriteLine("Credentials Valid");
                        Mission_Thread_shouldStop = false;
                    }
                    HttpResponseMessage SDAresp = await client.GetAsync("/api/missions");
                    Console.WriteLine(SDAresp.Content.ReadAsStringAsync().Result);

                    List<Mission> Server_Mission = new JavaScriptSerializer().Deserialize<List<Mission>>(SDAresp.Content.ReadAsStringAsync().Result);

                    int count = Mission_List.Count();
                    //Add obtained missions to the current list of missions 
                    for (int i = 0; i < Server_Mission.Count(); i++)
                    {
                        Server_Mission[i].name = "Server Mission_" + Convert.ToString(i + count);
                        Mission_List.Add(Server_Mission[i]);
                    }

                }
            }
            catch
            {
                Console.WriteLine("Error, exception thrown in Obstacle_SDA Thread");
            }
            Console.WriteLine("Mission_Download Thread Stopped");
        }

        public void Map_Control()
        {
            Console.WriteLine("Map_Control Thread Started");
            Stopwatch t = new Stopwatch();
            Stopwatch FlightTime = new Stopwatch();
            double GroundElevation;
            t.Start();

            //Add static overlays:
            //For testing right now. Will update when server has misison functionality added
            if (false)
            {
                List<Waypoint> Op_Area = new List<Waypoint>();
                Op_Area.Add(new Waypoint(38.1462694, -76.4277778));
                Op_Area.Add(new Waypoint(38.1516250, -76.4286833));
                Op_Area.Add(new Waypoint(38.1518889, -76.4314667));
                Op_Area.Add(new Waypoint(38.1505944, -76.4353611));
                Op_Area.Add(new Waypoint(38.1475667, -76.4323417));
                Op_Area.Add(new Waypoint(38.1446667, -76.4329472));
                Op_Area.Add(new Waypoint(38.1432556, -76.4347667));
                Op_Area.Add(new Waypoint(38.1404639, -76.4326361));
                Op_Area.Add(new Waypoint(38.1407194, -76.4260139));
                Op_Area.Add(new Waypoint(38.1437611, -76.4212056));
                Op_Area.Add(new Waypoint(38.1473472, -76.4232111));
                Op_Area.Add(new Waypoint(38.1461306, -76.4266528));

                //We clear becaues the construtor creates an empty flyzone.
                Current_Mission.fly_zones.Clear();
                Current_Mission.fly_zones.Add(new FlyZone(600, 100, Op_Area));

                Current_Mission.search_grid_points.Add(new Waypoint(38.1457306, -76.4295972));
                Current_Mission.search_grid_points.Add(new Waypoint(38.1431861, -76.4338917));
                Current_Mission.search_grid_points.Add(new Waypoint(38.1410028, -76.4322333));
                Current_Mission.search_grid_points.Add(new Waypoint(38.1411917, -76.4269806));
                Current_Mission.search_grid_points.Add(new Waypoint(38.1422194, -76.4261111));

                Current_Mission.air_drop_pos = new GPS_Position(38.145852, -76.426416);
                Current_Mission.off_axis_target_pos = new GPS_Position(38.147408, -76.433651);
                Current_Mission.emergent_lkp = new GPS_Position(38.144187, -76.423645);
            }


            List<Waypoint> Waypoints = new List<Waypoint>();
            Waypoints.Add(new Waypoint(38.147720, -76.429610));
            Waypoints.Add(new Waypoint(38.150893, -76.432056));
            Waypoints.Add(new Waypoint(38.149559, -76.434159));
            Waypoints.Add(new Waypoint(38.145729, -76.430275));
            Waypoints.Add(new Waypoint(38.143147, -76.428344));
            Waypoints.Add(new Waypoint(38.142168, -76.429482));
            Waypoints.Add(new Waypoint(38.144176, -76.431584));
            Waypoints.Add(new Waypoint(38.143214, -76.433151));
            Waypoints.Add(new Waypoint(38.141898, -76.432593));
            Waypoints.Add(new Waypoint(38.143063, -76.429675));
            Waypoints.Add(new Waypoint(38.144531, -76.426542));
            Waypoints.Add(new Waypoint(38.146471, -76.422379));
            Waypoints.Add(new Waypoint(38.144801, -76.421757));
            Waypoints.Add(new Waypoint(38.143029, -76.421692));
            Waypoints.Add(new Waypoint(38.142084, -76.423817));
            Waypoints.Add(new Waypoint(38.142016, -76.425469));
            Waypoints.Add(new Waypoint(38.145189, -76.428537));

            while (!Map_Control_Thread_shouldStop)
            {
                Interoperability_GUI.MAP_Clear_Overlays();
                //Draw Obstacles 
                if (Obstacles_Downloaded)
                {
                    if (Interoperability_GUI.getDrawObstacles())
                    {
                        for (int i = 0; i < obstaclesList.stationary_obstacles.Count(); i++)
                        {
                            Interoperability_GUI.MAP_addSObstaclePoly(obstaclesList.stationary_obstacles[i].cylinder_radius * 0.3048,
                                obstaclesList.stationary_obstacles[i].cylinder_height * 0.3048, obstaclesList.stationary_obstacles[i].latitude,
                                obstaclesList.stationary_obstacles[i].longitude, "Static_Obstacle" + i.ToString());
                        }

                        for (int i = 0; i < obstaclesList.moving_obstacles.Count(); i++)
                        {
                            Interoperability_GUI.MAP_addMObstaclePoly(obstaclesList.moving_obstacles[i].sphere_radius * 0.3048,
                               obstaclesList.moving_obstacles[i].altitude_msl * 0.3048, obstaclesList.moving_obstacles[i].latitude,
                               obstaclesList.moving_obstacles[i].longitude, "Moving_Obstacle" + i.ToString());
                        }
                    }
                }

                //Draw geofence
                if (Interoperability_GUI.getDrawGeofence())
                {
                    //Using first boundary point (assuming there is only one geofence) COME BACK TO THIS LATER
                    Interoperability_GUI.MAP_addStaticPoly(Current_Mission.fly_zones[0].boundary_pts, "Geofence", Color.Red, Color.Transparent, 3, 50);
                }

                //Draw search area
                if (Interoperability_GUI.getDrawSearchArea())
                {
                    Interoperability_GUI.MAP_addStaticPoly(Current_Mission.search_grid_points, "Search_Area", Color.Green, Color.Green, 3, 90);
                }

                //Draw plane location                   
                if (Interoperability_GUI.getDrawPlane())
                {
                    if (usePlaneSimulator == true)
                    {
                        Interoperability_GUI.MAP_updatePlaneLoc(new PointLatLng(sim_lat, sim_lng), sim_alt, sim_yaw, sim_yaw, sim_yaw, sim_yaw, 0);
                    }
                    else
                    {
                        Interoperability_GUI.MAP_updatePlaneLoc(new PointLatLng(Host.cs.lat, Host.cs.lng), Host.cs.alt, Host.cs.yaw,
                                                Host.cs.groundcourse, Host.cs.nav_bearing, Host.cs.target_bearing, Host.cs.radius);
                    }

                }
                //Draw Waypoints
                if (Interoperability_GUI.getDrawWP())
                {
                    if (usePlaneSimulator == true)
                    {
                        Interoperability_GUI.MAP_addWP(Current_Mission.all_waypoints);
                        //Interoperability_GUI.MAP_updateWPRoute(Current_Mission.all_waypoints);
                        Interoperability_GUI.MAP_addWPRoute(Simulator_Path);
                    }
                    else
                    {
                        //Draw waypoints
                        Interoperability_GUI.MAP_addWP(Waypoints);
                        //Draw lines between waypoints
                        Interoperability_GUI.MAP_addWPRoute(Waypoints);
                    }

                }

                //Draw off axis targets, emergent targets, and air drop location
                if (Interoperability_GUI.getDrawOFAT_EN_DROP())
                {
                    Interoperability_GUI.MAP_updateOFAT_EM_DROP(Current_Mission);
                }

                if (Interoperability_GUI.getAutopan())
                {
                    if (usePlaneSimulator == true)
                    {
                        Interoperability_GUI.MAP_Update_Loc(new PointLatLng(sim_lat, sim_lng));
                    }
                    else
                    {
                        Interoperability_GUI.MAP_Update_Loc(new PointLatLng(Host.cs.lat, Host.cs.lng));
                    }

                }

                Interoperability_GUI.MAP_Update_Overlay();

                //Draw Images if preset

                //foreach 


                //Update GPS Location label at bottom of interface
                switch (geo_cords)
                {
                    case "DD.DDDDDD":
                        Interoperability_GUI.MAP_updateGPSLabel(Host.cs.lat.ToString("00.000000") + " " + Host.cs.lng.ToString("00.000000"));
                        break;
                    case "DD MM SS.SS":
                        Interoperability_GUI.MAP_updateGPSLabel(DDtoDMS(Host.cs.lat, Host.cs.lng));
                        break;
                    default:
                        Interoperability_GUI.MAP_updateGPSLabel(Host.cs.lat.ToString("00.000000") + " " + Host.cs.lng.ToString("00.000000"));
                        break;
                }

                GroundElevation = srtm.getAltitude(Host.cs.lat, Host.cs.lng).alt;

                //Update altitude and delta altitude label at bottom of interface
                switch (dist_units)
                {
                    case "Metres":
                        Interoperability_GUI.MAP_updateAltLabel(Host.cs.altasl.ToString("00.000") + "m",
                            (Host.cs.altasl - GroundElevation).ToString("00.000") + "m");
                        break;
                    case "Feet":
                        Interoperability_GUI.MAP_updateAltLabel((3.28084 * Host.cs.altasl).ToString("00.000") + "ft",
                            (3.28084 * Host.cs.altasl - 3.28084 * GroundElevation).ToString("00.000") + "ft");
                        break;
                    default:
                        break;
                }
                Interoperability_GUI.setFlightTimerLabel(FlightTime.ElapsedMilliseconds);
                //Console.WriteLine(srtm.getAltitude(Host.cs.lat, Host.cs.lng).alt.ToString());
                t.Restart();

                //It's okay if we call this multiple times, because it just resumes the flight timer
                if (Host.cs.airspeed > 10)
                {
                    FlightTime.Start();
                }
                else
                {
                    FlightTime.Stop();
                }
                if (resetFlightTimer == true)
                {
                    FlightTime.Reset();
                    resetFlightTimer = false;
                }
                Thread.Sleep(1000 / Interoperability_GUI.getMapRefreshRate());
            }
            Console.WriteLine("Map_Control Thread Stopped");
        }

        public void Callouts()
        {
            Stopwatch t = new Stopwatch();
            t.Start();
            //Set up speech output 
            SpeechSynthesizer Speech = new SpeechSynthesizer();

            Console.WriteLine("Callout Thread Started");

            using (SpeechRecognitionEngine recognizer = new SpeechRecognitionEngine(new System.Globalization.CultureInfo("en-US")))
            {
                Choices Modes = new Choices();
                Modes.Add(new string[] { "landing", "taking off", "airspeed", "altitude", "flight time" });

                // Create a GrammarBuilder object and append the Choices object.
                GrammarBuilder gb = new GrammarBuilder();
                gb.Append(Modes);
                Grammar g = new Grammar(gb);
                recognizer.LoadGrammar(g);
                //Voice recognition allows 
                recognizer.SpeechRecognized += new EventHandler<SpeechRecognizedEventArgs>(recognizer_SpeechRecognized);
                recognizer.SetInputToDefaultAudioDevice();

                bool Recognizer_Enabled = false;
                string CalloutMode = "";

                while (Callout_Thread_shouldStop == false)
                {
                    if (Interoperability_GUI.getSpeechRecognition_Enabled() == true && !Recognizer_Enabled)
                    {
                        recognizer.RecognizeAsync(RecognizeMode.Multiple);
                        Recognizer_Enabled = true;
                    }
                    else if (Interoperability_GUI.getSpeechRecognition_Enabled() == false)
                    {
                        recognizer.RecognizeAsyncStop();
                        Recognizer_Enabled = false;
                    }

                    //Probably shouldn't be calling this in an infinite loop
                    CalloutMode = Interoperability_GUI.getCalloutMode();
                    if (CalloutMode == "Landing")
                    {

                    }
                    else if (CalloutMode == "Takeoff")
                    {

                    }
                    //Thread.Sleep(10000000);
                    if (t.ElapsedMilliseconds > (Interoperability_GUI.getCalloutPeriod()))
                    {
                        //If we speak asynchronously, then we might speak over the previous speach  
                        Speech.SpeakAsync(Convert.ToInt32(Host.cs.airspeed).ToString());
                        t.Restart();
                    }
                }

            }
            Console.WriteLine("Callout thread has stopped");
        }

        private void recognizer_SpeechRecognized(object sender, SpeechRecognizedEventArgs e)
        {
            MessageBox.Show("Speech recognized: " + e.Result.Text);
        }


        /// <summary>
        /// Returns Degree Minute Seconds string given Decimal Degrees
        /// </summary>
        /// <param name="lat">Latitude</param>
        /// <param name="lng">Longitude</param>
        /// <returns></returns>
        public static string DDtoDMS(double lat, double lng)
        {
            string DMS;

            double minutes = Math.Floor((Math.Abs(lat) - Math.Floor(Math.Abs(lat))) * 60.0);
            double seconds = (Math.Abs(lat) - Math.Floor(Math.Abs(lat)) - minutes / 60) * 3600;


            if (lat > 0)
            {
                DMS = "N";
            }
            else
            {
                DMS = "S";
            }
            DMS += Convert.ToInt32(Math.Abs(lat)).ToString("00") + "-" + Math.Abs(minutes).ToString("00") + "-" + Math.Abs(seconds).ToString("00.00") + " ";// + "." + tenths.ToString("00") + " ";

            minutes = Math.Floor((Math.Abs(lng) - Math.Floor(Math.Abs(lng))) * 60.0);
            seconds = (Math.Abs(lng) - Math.Floor(Math.Abs(lng)) - minutes / 60) * 3600;

            if (lng > 0)
            {
                DMS += "E";
            }
            else
            {
                DMS += "W";
            }
            DMS += Convert.ToInt32(Math.Abs(lng)).ToString("000") + "-" + Math.Abs(minutes).ToString("00") + "-" + Math.Abs(seconds).ToString("00.00"); //+ "." + tenths.ToString("00");
            return DMS;
        }

        /// <summary>
        /// Returns Decimal Degrees given a Degree Minute Seconds string
        /// </summary>
        /// <param name="lat">Latitude in DMS "W36-28-45.67"</param>
        /// <param name="lng">Longitude in DMS "S038-94-12.89"</param>
        /// <returns></returns>
        public static Waypoint DMStoDD(string lat, string lng)
        {
            double DD_Lat, DD_Lng;
            Waypoint Converted_DD = new Waypoint();

            //Assuming the format is correct
            char[] delimiterChars = { '-' };

            string[] lat_split = lat.Split(delimiterChars);
            string[] lng_split = lng.Split(delimiterChars);
            //43.236403, -98.927366
            try
            {
                double lat_degrees = Convert.ToDouble(lat_split[0][1].ToString() + lat_split[0][2].ToString());
                double lat_minutes = Convert.ToDouble(lat_split[1].ToString()) / 60;
                double lat_seconds = Convert.ToDouble(lat_split[2]) / 3600;
                DD_Lat = lat_degrees + lat_minutes + lat_seconds;

                if (lat_split[0][0] == 'S')
                {
                    DD_Lat *= -1;
                }

                double lng_degrees = Convert.ToDouble(lng_split[0][1].ToString() + lng_split[0][2].ToString() + lng_split[0][3].ToString());
                double lng_minutes = Convert.ToDouble(lng_split[1].ToString()) / 60;
                double lng_seconds = Convert.ToDouble(lng_split[2]) / 3600;
                DD_Lng = lng_degrees + lng_minutes + lng_seconds;

                if (lng_split[0][0] == 'W')
                {
                    DD_Lng *= -1;
                }
            }
            catch
            {
                //Something went wrong with the format I think
                return Converted_DD;
            }

            return new Waypoint(DD_Lat, DD_Lng);
        }

        // BE CAREFUL, THIS IS SKETCHY AS FUCK
        // We also don't need this until later :) 
        public /*virtual int*/ async void TrollLoop(/*StreamWriter writer,*/ HttpClient client)
        {
            //Console.WriteLine("LOOP TIME -> " + DateTime.Now.ToString());

            CurrentState cs = this.Host.cs;
            double lat = cs.lat, lng = cs.lng, alt = cs.altasl, yaw = cs.yaw;

            var v = new Dictionary<string, string>();
            v.Add("latitude", lat.ToString("F10"));
            v.Add("longitude", lng.ToString("F10"));
            v.Add("altitude_msl", alt.ToString("F10"));
            v.Add("uas_heading", yaw.ToString("F10"));
            //Console.WriteLine("Latitude: " + lat + "\nLongitude: " + lng + "\nAltitude_MSL: " + alt + "\nHeading: " + yaw);

            var telem = new FormUrlEncodedContent(v);
            HttpResponseMessage telemresp = await client.PostAsync("/api/telemetry", telem);
            Console.WriteLine("Server_info GET result: " + telemresp.Content.ReadAsStringAsync().Result);

            return;
        }

        /// <summary>
        /// Load your own code here, this is only run once on loading
        /// </summary>
        /// <returns></returns>
        override public bool Loaded()
        {
            Console.WriteLine("* * * * * Interoperability plugin loaded. * * * * *");

            // Attempt to login to server?

            return (true);
        }

        /// <summary>
        /// Run at NextRun time - loop is run in a background thread. and is shared with other plugins
        /// Ensure threads that unintentially die are revived
        /// </summary>
        /// <returns></returns>
        override public bool Loop()
        {
            /*if (Telemetry_Upload_shouldStop == false && !Telemetry_Upload_isAlive)
            {
                interoperabilityAction(0);
            }
            if (Obstacle_SDA_shouldStop == false && !Obstacle_SDA_isAlive)
            {
                interoperabilityAction(1);
            }
            if (Map_Control_shouldStop == false && !Map_Control_isAlive)
            {
                interoperabilityAction(6);
            }*/

            return true;
        }

        /// <summary>
        /// Exit is only called on plugin unload. not app exit
        /// </summary>
        /// <returns></returns>
        override public bool Exit()
        {
            interoperabilityAction(Interop_Action.Stop_All_Threads_Quit);
            return (true);
        }
    }
}