﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using System.IO;
using System.Threading;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using GMap.NET;
using GMap.NET.WindowsForms;
using GMap.NET.WindowsForms.Markers;

namespace interoperability
{
    public class Interoperability_Settings
    {
        static Interoperability_Settings _instance;
        private static Mutex Settings_XML_Mutex = new Mutex(); //So we only write settings one at a time;

        public static Interoperability_Settings Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new Interoperability_Settings();
                }
                return _instance;
            }
        }

        public Interoperability_Settings()
        {
        }

        /// <summary>
        /// use to store all internal config
        /// </summary>
        public static Dictionary<string, string> config = new Dictionary<string, string>();

        const string FileName = "Interoperability_Config.xml";

        public string this[string key]
        {
            get
            {
                string value = null;
                config.TryGetValue(key, out value);
                return value;
            }

            set
            {
                config[key] = value;
            }
        }

        public IEnumerable<string> Keys
        {
            // the "ToArray" makes this safe for someone to add items while enumerating.
            get { return config.Keys.ToArray(); }
        }
        public bool ContainsKey(string key)
        {
            return config.ContainsKey(key);
        }



        public int Count { get { return config.Count; } }


        internal int GetInt32(string key)
        {
            int result = 0;
            string value = null;
            if (config.TryGetValue(key, out value))
            {
                int.TryParse(value, out result);
            }
            return result;
        }

        internal bool GetBoolean(string key)
        {
            bool result = false;
            string value = null;
            if (config.TryGetValue(key, out value))
            {
                bool.TryParse(value, out result);
            }
            return result;
        }

        internal float GetFloat(string key)
        {
            float result = 0f;
            string value = null;
            if (config.TryGetValue(key, out value))
            {
                float.TryParse(value, out result);
            }
            return result;
        }

        internal double GetDouble(string key)
        {
            double result = 0D;
            string value = null;
            if (config.TryGetValue(key, out value))
            {
                double.TryParse(value, out result);
            }
            return result;
        }

        internal byte GetByte(string key)
        {
            byte result = 0;
            string value = null;
            if (config.TryGetValue(key, out value))
            {
                byte.TryParse(value, out result);
            }
            return result;
        }

        public static string GetFullPath()
        {
            string directory = Path.GetDirectoryName(Application.ExecutablePath) + Path.DirectorySeparatorChar;
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            return Path.Combine(directory, FileName);
        }

        public void Load()
        {
            if (File.Exists(GetFullPath()))
            {
                Settings_XML_Mutex.WaitOne();
                using (XmlTextReader xmlreader = new XmlTextReader(GetFullPath()))
                {
                    while (xmlreader.Read())
                    {
                        if (xmlreader.NodeType == XmlNodeType.Element)
                        {
                            try
                            {
                                switch (xmlreader.Name)
                                {
                                    case "Config":
                                        break;
                                    case "xml":
                                        break;
                                    default:
                                        config[xmlreader.Name] = xmlreader.ReadString();
                                        break;
                                }
                            }
                            // silent fail on bad entry
                            catch (Exception)
                            {
                            }
                        }
                    }
                }
                Settings_XML_Mutex.ReleaseMutex();
            }

        }

        public void Save()
        {
            string filename = GetFullPath();
            Settings_XML_Mutex.WaitOne();
            using (XmlTextWriter xmlwriter = new XmlTextWriter(filename, Encoding.UTF8))
            {
                xmlwriter.Formatting = Formatting.Indented;

                xmlwriter.WriteStartDocument();

                xmlwriter.WriteStartElement("Config");

                foreach (string key in config.Keys)
                {
                    try
                    {
                        if (key == "" || key.Contains("/")) // "/dev/blah"
                            continue;

                        xmlwriter.WriteElementString(key, "" + config[key]);
                    }
                    catch
                    {
                    }
                }

                xmlwriter.WriteEndElement();

                xmlwriter.WriteEndDocument();
                xmlwriter.Close();
            }
            Settings_XML_Mutex.ReleaseMutex();
        }

        public void Remove(string key)
        {
            if (config.ContainsKey(key))
            {
                config.Remove(key);
            }
        }

    }

    public static class MercatorProjection
    {
        private static readonly double R_MAJOR = 6378137.0;
        private static readonly double R_MINOR = 6356752.3142;
        private static readonly double RATIO = R_MINOR / R_MAJOR;
        private static readonly double ECCENT = Math.Sqrt(1.0 - (RATIO * RATIO));
        private static readonly double COM = 0.5 * ECCENT;

        private static readonly double DEG2RAD = Math.PI / 180.0;
        private static readonly double RAD2Deg = 180.0 / Math.PI;
        private static readonly double PI_2 = Math.PI / 2.0;

        public static double[] toPixel(double lon, double lat)
        {
            return new double[] { lonToX(lon), latToY(lat) };
        }

        public static double[] toGeoCoord(double x, double y)
        {
            return new double[] { xToLon(x), yToLat(y) };
        }

        public static double lonToX(double lon)
        {
            return R_MAJOR * DegToRad(lon);
        }

        public static double latToY(double lat)
        {
            lat = Math.Min(89.5, Math.Max(lat, -89.5));
            double phi = DegToRad(lat);
            double sinphi = Math.Sin(phi);
            double con = ECCENT * sinphi;
            con = Math.Pow(((1.0 - con) / (1.0 + con)), COM);
            double ts = Math.Tan(0.5 * ((Math.PI * 0.5) - phi)) / con;
            return 0 - R_MAJOR * Math.Log(ts);
        }

        public static double xToLon(double x)
        {
            return RadToDeg(x) / R_MAJOR;
        }

        public static double yToLat(double y)
        {
            double ts = Math.Exp(-y / R_MAJOR);
            double phi = PI_2 - 2 * Math.Atan(ts);
            double dphi = 1.0;
            int i = 0;
            while ((Math.Abs(dphi) > 0.000000001) && (i < 15))
            {
                double con = ECCENT * Math.Sin(phi);
                dphi = PI_2 - 2 * Math.Atan(ts * Math.Pow((1.0 - con) / (1.0 + con), COM)) - phi;
                phi += dphi;
                i++;
            }
            return RadToDeg(phi);
        }

        private static double RadToDeg(double rad)
        {
            return rad * RAD2Deg;
        }

        private static double DegToRad(double deg)
        {
            return deg * DEG2RAD;
        }
    }

}

