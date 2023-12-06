using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Media3D;
using Newtonsoft.Json;
using static thermalCamera.MainWindow;


namespace thermalCamera
{
    public partial class CalibrationWindow : Window
    {
        private List<CameraCalibrationData> allCalibrationData;
        private string calibrationFilePath;
        private string cameraId;
        public List<DualCameraCalibrationPoint> CalibrationPoints { get; set; }

        public CalibrationWindow(string calibrationFilePath, string cameraId)
        {
            InitializeComponent();
            allCalibrationData = new List<CameraCalibrationData>();
            this.calibrationFilePath = calibrationFilePath;
            LoadCalibrationData();
        }

        private void LoadCalibrationData()
        {
            string json = File.ReadAllText(calibrationFilePath);
            allCalibrationData = JsonConvert.DeserializeObject<List<CameraCalibrationData>>(json) ?? new List<CameraCalibrationData>();

            var camera1Data = allCalibrationData.FirstOrDefault(c => c.CameraId == "camera1");
            if (camera1Data == null)
            {
                camera1Data = new CameraCalibrationData { CameraId = "camera1", Points = new List<CalibrationPoint>() };
                allCalibrationData.Add(camera1Data);
            }

            var camera2Data = allCalibrationData.FirstOrDefault(c => c.CameraId == "camera2");
            if (camera2Data == null)
            {
                camera2Data = new CameraCalibrationData { CameraId = "camera2", Points = new List<CalibrationPoint>() };
                allCalibrationData.Add(camera2Data);
            }

            // Create a list of DualCameraCalibrationPoint from camera1Data and camera2Data
            CalibrationPoints = new List<DualCameraCalibrationPoint>();
            for (int i = 0; i < Math.Max(camera1Data.Points.Count, camera2Data.Points.Count); i++)
            {
                CalibrationPoints.Add(new DualCameraCalibrationPoint
                {
                    Camera1 = i < camera1Data.Points.Count ? camera1Data.Points[i] : new CalibrationPoint(),
                    Camera2 = i < camera2Data.Points.Count ? camera2Data.Points[i] : new CalibrationPoint()
                });
            }

            calibrationDataGrid.ItemsSource = CalibrationPoints;
        }



        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // Exclude the last entry if it is empty
            if (CalibrationPoints.Any() && IsDefaultCalibrationPoint(CalibrationPoints.Last()))
            {
                CalibrationPoints.RemoveAt(CalibrationPoints.Count - 1);
            }

            // Extract camera1Data and camera2Data from allCalibrationData
            var camera1Data = allCalibrationData.FirstOrDefault(c => c.CameraId == "camera1");
            var camera2Data = allCalibrationData.FirstOrDefault(c => c.CameraId == "camera2");

            // Proceed only if both are not null
            if (camera1Data != null && camera2Data != null)
            {
                // Update camera1Data.Points and camera2Data.Points based on CalibrationPoints
                // ...

                // Serialize and save
                string json = JsonConvert.SerializeObject(allCalibrationData);
                File.WriteAllText(calibrationFilePath, json);
                MessageBox.Show("Calibration data saved.");
            }

            this.Close();

            // Consider re-adding an empty DualCameraCalibrationPoint to CalibrationPoints if needed
        }




        private bool IsDefaultCalibrationPoint(DualCameraCalibrationPoint dcp)
        {
            // Check if both Camera1 and Camera2 are default/empty
            return IsDefaultCalibrationPoint(dcp.Camera1) && IsDefaultCalibrationPoint(dcp.Camera2);
        }
        private bool IsDefaultCalibrationPoint(CalibrationPoint point)
        {
            // Define what constitutes a default/empty CalibrationPoint
            return point == null || (point.RawValue == default(double) && point.ReferenceTemperature == default(double));
        }
    }
}
