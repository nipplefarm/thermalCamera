using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Image = System.Windows.Controls.Image;
using MessageBox = System.Windows.MessageBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Drawing.Point;


namespace thermalCamera
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // Initiate flags
        private bool isRecording = false;
        private String selectedFolderPath = "";
        private string recordingFolderPath = "";
        private VideoCapture? camera1Capture;
        private VideoCapture? camera2Capture;
        private bool isCapturing = false;
        private int camera1FileCounter = 0;
        private int camera2FileCounter = 0;
        private Mat latestFrameCamera1; // Add this line
        private Mat latestFrameCamera2; // If you have a second camera
        private Dictionary<string, List<CalibrationPoint>> calibrationData;
        private bool isMouseOverImage = false;
        private System.Windows.Threading.DispatcherTimer temperatureUpdateTimer;
        private String calibrationFilePath = "Data/calibrationData.json";

        public MainWindow()
        {
            InitializeComponent();
            PopulateCameraSelection();
            recordButton.IsEnabled = false; // Disable record button initially
            directoryMessageTextBlock.Visibility = selectedFolderPath == "" ? Visibility.Visible : Visibility.Collapsed;
            calibrationData = LoadCalibrationData(calibrationFilePath);
            temperatureUpdateTimer = new System.Windows.Threading.DispatcherTimer();
            temperatureUpdateTimer.Tick += TemperatureUpdateTimer_Tick;
            temperatureUpdateTimer.Interval = TimeSpan.FromMilliseconds(250); // Update every 500 milliseconds
            temperatureUpdateTimer.Start();
            calibrationData = LoadCalibrationData(calibrationFilePath);

        }


        // populates the  list of availbale camera indices
        private void PopulateCameraSelection()
        {
            int index = 0;
            List<int> cameraIndices = new List<int>();
            while (true)
            {
                using (var tempCapture = new VideoCapture(index))
                {
                    if (tempCapture.IsOpened)
                    {
                        cameraIndices.Add(index);
                        index++;
                    }
                    else
                    {
                        break; // No more cameras
                    }
                }
            }

            camera1Selector.ItemsSource = cameraIndices;
            camera2Selector.ItemsSource = new List<int>(cameraIndices); // Create a copy for camera2Selector

            camera1Selector.SelectionChanged += CameraSelectionChanged;
            camera2Selector.SelectionChanged += CameraSelectionChanged;
        }


        // Enables start/stop button when a camera is selected.
        private void CameraSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is System.Windows.Controls.ComboBox comboBox)
            {
                startStopButton.IsEnabled = camera1Selector.SelectedIndex != -1 || camera2Selector.SelectedIndex != -1;
            }
        }

        // Starts/stops the cameras when the start button is clicked
        private async void StartStopButton_Click(object sender, RoutedEventArgs e)
        {
            if (!isCapturing)
            {
                await StartCamerasAsync();
                startStopButton.Content = "Stop";
                recordButton.IsEnabled = true;
            }
            else
            {
                StopCameras();
                startStopButton.Content = "Start";
                recordButton.IsEnabled = false;
            }
            isCapturing = !isCapturing;
            UpdateRecordButtonState();
        }

        // Starts the cameras and updates temperatures
        private async Task StartCamerasAsync()
        {
            int camera1Index = camera1Selector.SelectedIndex;
            int camera2Index = camera2Selector.SelectedIndex;

            // Release any existing resources
            ReleaseCameraResources();

            await Task.Run(() =>
            {
                // Initialize and start the first camera
                if (camera1Index >= 0)
                {
                    camera1Capture = new VideoCapture(camera1Index, VideoCapture.API.DShow);
                    SetCameraToY16(camera1Capture);
                    camera1Capture.ImageGrabbed += ProcessFrameCamera1;
                    camera1Capture.Start();
                    if (camera1Capture != null && camera1Capture.Ptr != IntPtr.Zero)
                    {
                        UpdateCenterPixelTemperature(latestFrameCamera1, camera1TemperatureTextBlock, "camera1");
                    }

                }

                // Initialize and start the second camera
                if (camera2Index >= 0)
                {
                    camera2Capture = new VideoCapture(camera2Index, VideoCapture.API.DShow);
                    SetCameraToY16(camera2Capture);
                    camera2Capture.ImageGrabbed += ProcessFrameCamera2;
                    camera2Capture.Start();
                    if (camera2Capture != null && camera2Capture.Ptr != IntPtr.Zero)
                    {
                        UpdateCenterPixelTemperature(latestFrameCamera2, camera2TemperatureTextBlock, "camera2");
                    }
                }
            });
        }

        // Sets the camera to Y16 and turns off auto RGB conversion
        private void SetCameraToY16(VideoCapture capture)
        {
            if (capture != null)
            {
                // Set the camera to capture in Y16 format
                int fourcc = VideoWriter.Fourcc('Y', '1', '6', ' ');
                capture.Set(CapProp.FourCC, fourcc);

                // Disable automatic conversion to RGB
                capture.Set(CapProp.ConvertRgb, 0);

                // Debug: Read back the properties to ensure they are set correctly
                int readFourcc = (int)capture.Get(CapProp.FourCC);
                int readConvertRgb = (int)capture.Get(CapProp.ConvertRgb);

            }
        }

        // Stop camera logic
        private void StopCameras()
        {
            camera1Capture?.Stop();
            camera1Capture?.Dispose();
            camera1Capture = null;

            camera2Capture?.Stop();
            camera2Capture?.Dispose();
            camera2Capture = null;
            ReleaseCameraResources();
        }

        // Releases camera and its resources
        private void ReleaseCameraResources()
        {
            if (camera1Capture != null)
            {
                camera1Capture.Dispose();
                camera1Capture = null;
            }

            if (camera2Capture != null)
            {
                camera2Capture.Dispose();
                camera2Capture = null;
            }
        }

        // Processes frames from cameras (saves file, Gets temperature, converts to RGB)
        private void ProcessFrameCamera1(object sender, EventArgs e)
        {
            if (camera1Capture != null && camera1Capture.Ptr != IntPtr.Zero)
            {
                if (latestFrameCamera1 == null)
                    latestFrameCamera1 = new Mat();

                camera1Capture.Retrieve(latestFrameCamera1);

                Mat croppedFrame1 = CropImage(latestFrameCamera1, 160, 120);
                if (isRecording)
                {
                    string fileName = $"camera1_{camera1FileCounter:D4}.tiff";
                    string filePath = Path.Combine(recordingFolderPath, fileName);
                    camera1FileCounter++;
                    croppedFrame1.Save(filePath); // Save the raw Y16 frame
                }
                if (!isMouseOverImage)
                {
                    UpdateCenterPixelTemperature(croppedFrame1, camera1TemperatureTextBlock, "camera1");
                }
                // Convert the Y16 frame for display
                Mat displayableFrame = ConvertY16ToDisplayableRgb(croppedFrame1);

                Dispatcher.Invoke(() =>
                {
                    // Check if the application is still running
                    if (System.Windows.Application.Current != null && !Dispatcher.HasShutdownStarted)
                    {
                        try
                        {
                            // Update the image source
                            camera1Image.Source = ToBitmapSource(displayableFrame);
                        }
                        catch (TaskCanceledException)
                        {
                        }
                    }
                });
            }
        }

        // Same as above but for second camera
        private void ProcessFrameCamera2(object sender, EventArgs e)
        {
            if (camera2Capture != null && camera2Capture.Ptr != IntPtr.Zero)
            {
                if (latestFrameCamera2 == null)
                    latestFrameCamera2 = new Mat();

                camera2Capture.Retrieve(latestFrameCamera2);
                Mat croppedFrame2 = CropImage(latestFrameCamera2, 160, 120);

                if (isRecording)
                {
                    string fileName = $"camera2_{camera2FileCounter:D4}.tiff";
                    string filePath = Path.Combine(recordingFolderPath, fileName);
                    camera2FileCounter++;
                    croppedFrame2.Save(filePath); // Save the raw Y16 frame
                }
                if (!isMouseOverImage)
                {
                    UpdateCenterPixelTemperature(croppedFrame2, camera2TemperatureTextBlock, "camera2");
                }
                // Convert the Y16 frame for display
                Mat displayableFrame = ConvertY16ToDisplayableRgb(croppedFrame2);

                Dispatcher.Invoke(() =>
                {
                    // Check if the application is still running
                    if (System.Windows.Application.Current != null && !Dispatcher.HasShutdownStarted)
                    {
                        try
                        {
                            // Update the image source
                            camera2Image.Source = ToBitmapSource(displayableFrame);
                        }
                        catch (TaskCanceledException)
                        {
                        }
                    }
                });
            }
        }

        // Converts from Y16 to RGB, min and maxVal set range for normalization to keep cameras comparable. (value is in centiKelvin)
        private Mat ConvertY16ToDisplayableRgb(Mat y16Image, double minVal = 29115, double maxVal = 37315)
        {
            if (y16Image == null || y16Image.IsEmpty)
                return new Mat();

            // Ensure the image is 16-bit single-channel
            if (y16Image.Depth != DepthType.Cv16U || y16Image.NumberOfChannels != 1)
            {
                throw new InvalidOperationException("Expected Y16 format (16-bit single-channel)");
            }

            // Normalize the 16-bit image to 8-bit using the specified raw value range
            Mat normalizedImage = new Mat();
            CvInvoke.Normalize(y16Image, normalizedImage, 0, 255, NormType.MinMax, DepthType.Cv8U);

            // Apply a colormap for better visualization
            Mat coloredImage = new Mat();
            CvInvoke.ApplyColorMap(normalizedImage, coloredImage, ColorMapType.Jet);

            return coloredImage;
        }



        // Gets temperature of pixel at x,y
        private double GetPixelValue(Mat frame, int x, int y, string cameraId)
        {
            if (frame == null || frame.IsEmpty || x < 0 || y < 0 || x >= frame.Cols || y >= frame.Rows)
            {
                throw new ArgumentException("Invalid frame or pixel coordinates.");
            }

            // Read 16-bit value (2 bytes) for Y16 format
            IntPtr pixelPtr = frame.DataPointer + y * frame.Step + x * frame.ElementSize;
            ushort rawValue = (ushort)(Marshal.ReadByte(pixelPtr) | (Marshal.ReadByte(pixelPtr + 1) << 8));

            // Apply calibration directly with rawValue (in centiKelvin)
            if (calibrationData.TryGetValue(cameraId, out var cameraCalibrationPoints))
            {
                return ApplyCalibration(rawValue, cameraCalibrationPoints);
            }

            // Convert to Celsius for display or if no calibration data available
            return (double)rawValue / 100 - 273.15;
        }





        // Record button logic
        private void RecordButton_Click(object sender, RoutedEventArgs e)
        {
            isRecording = !isRecording;
            recordButton.Content = isRecording ? "Stop Recording" : "Record";

            if (isRecording)
            {
                if (String.IsNullOrEmpty(selectedFolderPath))
                {
                    MessageBox.Show("Please select a directory first.");
                    isRecording = false;
                    recordButton.Content = "Record";
                    return;
                }

                string selectedOption = "Default";
                if (choiceComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    selectedOption = selectedItem.Content.ToString()!;
                }

                recordingFolderPath = Path.Combine(selectedFolderPath, CreateRecordingFolderName(selectedOption));
                camera1FileCounter = 0;
                camera2FileCounter = 0;

                // Save calibration data
                SaveCalibrationData(recordingFolderPath);

                // Other recording start logic...
            }
            else
            {
                // Recording stop logic...
            }
        }

        private void SaveCalibrationData(string folderPath)
        {
            string calibrationFilePath = Path.Combine(folderPath, "calibrationData.json");
            // Directly serialize the calibration data without any conversion
            string jsonData = JsonConvert.SerializeObject(calibrationData, Formatting.Indented);
            File.WriteAllText(calibrationFilePath, jsonData);
        }



        // Updates Record button state
        private void UpdateRecordButtonState()
        {
            recordButton.IsEnabled = !string.IsNullOrEmpty(selectedFolderPath) && isCapturing;
        }
        private string CreateRecordingFolderName(string option)
        {
            string folderName = DateTime.Now.ToString("HH_mm-dd-MM-yy_") + option;
            Directory.CreateDirectory(Path.Combine(selectedFolderPath, folderName));
            return folderName;
        }

        // ComboBox selection (Only relevant to my work)
        private void ChoiceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

            if (sender is System.Windows.Controls.ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                // Check if the Content is not null before calling ToString()
                if (selectedItem.Content != null)
                {
                    string selectedChoice = selectedItem.Content.ToString()!;
                    Trace.WriteLine($"ComboBox selection changed: {selectedChoice}");
                }
                else
                {
                    Trace.WriteLine("Selected item's content is null");
                    // Handle the case when Content is null, if necessary
                }
            }
        }

        // Quit button logic
        private void QuitButton_Click(object sender, RoutedEventArgs e)
        {
            StopCameras();
            ReleaseCameraResources();
            System.Windows.Application.Current.Shutdown();
        }

        // Directoy button logic
        private void SelectDirectoryButton_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                DialogResult result = dialog.ShowDialog();

                if (result == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
                {
                    selectedFolderPath = dialog.SelectedPath;
                    recordButton.IsEnabled = isCapturing; // Enable the record button
                    directoryMessageTextBlock.Visibility = Visibility.Collapsed; // Hide the message
                }
            }
            UpdateRecordButtonState();
        }

        // update center pixel temp logic (Gives temperature of pixel in middle of frame) Can be used to get pixel value at arbitraty point.
        private void UpdateCenterPixelTemperature(Mat frame, TextBlock temperatureTextBlock, String cameraId)
        {
            if (frame != null && temperatureTextBlock != null)
            {
                int centerX = frame.Width / 2;
                int centerY = frame.Height / 2;
                double pixelValue = GetPixelValue(frame, centerX, centerY, cameraId);

                // Use Dispatcher to update the UI on the UI thread
                Dispatcher.Invoke(() =>
                {
                    temperatureTextBlock.Text = $"Center Temperature: {pixelValue.ToString("F2")}°C";
                });
            }
        }
        // Ticker so mouse and central point temp can be updated regularly
        private void TemperatureUpdateTimer_Tick(object sender, EventArgs e)
        {
            if (isMouseOverImage)
            {
                // Check which camera's image the mouse is currently over
                var mousePositionCamera1 = Mouse.GetPosition(camera1Image);
                var mousePositionCamera2 = Mouse.GetPosition(camera2Image);

                // Convert to System.Drawing.Point
                var drawingPointCamera1 = new System.Drawing.Point((int)mousePositionCamera1.X, (int)mousePositionCamera1.Y);
                var drawingPointCamera2 = new System.Drawing.Point((int)mousePositionCamera2.X, (int)mousePositionCamera2.Y);

                // Update temperature for the camera that the mouse is over
                if (camera1Image.IsMouseOver)
                {
                    UpdateTemperatureDisplay(latestFrameCamera1, camera1Image, camera1TemperatureTextBlock, drawingPointCamera1, "camera1");
                }
                else if (camera2Image.IsMouseOver)
                {
                    UpdateTemperatureDisplay(latestFrameCamera2, camera2Image, camera2TemperatureTextBlock, drawingPointCamera2, "camera2");
                }
            }
            else
            {
                // Update center pixel temperature for both cameras
                UpdateCenterPixelTemperature(latestFrameCamera1, camera1TemperatureTextBlock, "camera1");
                UpdateCenterPixelTemperature(latestFrameCamera2, camera2TemperatureTextBlock, "camera2");
            }
        }

        // Updates temperature text boxes based on mouse location
        private void UpdateTemperatureDisplay(Mat cameraFrame, Image cameraImage, TextBlock temperatureTextBlock, Point position, String cameraId)
        {
            double xScale = cameraImage.Source.Width / cameraImage.ActualWidth;
            double yScale = cameraImage.Source.Height / cameraImage.ActualHeight;

            int x = (int)(position.X * xScale);
            int y = (int)(position.Y * yScale);

            if (cameraFrame != null)
            {
                try
                {
                    double pixelValue = GetPixelValue(cameraFrame, x, y, cameraId);
                    Dispatcher.Invoke(() =>
                    {
                        temperatureTextBlock.Text = $"Temperature: {pixelValue.ToString("F2")}°C";
                    });
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Error getting pixel value: {ex.Message}");
                }
            }
        }


        // Mouse enter logic for frames
        private void Image_MouseEnter(object sender, MouseEventArgs e)
        {
            isMouseOverImage = true;
        }

        // mouse exit logic for frames
        private void Image_MouseLeave(object sender, MouseEventArgs e)
        {
            isMouseOverImage = false;
        }

        // mouse move logic for frames
        private void Image_MouseMove(object sender, MouseEventArgs e)
        {
            if (sender is Image image && image.Source != null)
            {
                System.Windows.Point position = e.GetPosition(image);
                double xScale = image.Source.Width / image.ActualWidth;
                double yScale = image.Source.Height / image.ActualHeight;

                int x = (int)(position.X * xScale);
                int y = (int)(position.Y * yScale);

                Mat? frameToUse;
                TextBlock? textBlockToUpdate;
                string cameraId;

                if (image.Equals(camera1Image))
                {
                    frameToUse = latestFrameCamera1;
                    textBlockToUpdate = camera1TemperatureTextBlock;
                    cameraId = "camera1"; // Set camera ID for camera 1
                }
                else if (image.Equals(camera2Image))
                {
                    frameToUse = latestFrameCamera2;
                    textBlockToUpdate = camera2TemperatureTextBlock;
                    cameraId = "camera2"; // Set camera ID for camera 2
                }
                else
                {
                    return; // Exit if the image is not one of the known cameras
                }

                if (frameToUse != null && textBlockToUpdate != null)
                {
                    UpdateTemperatureAtPosition(frameToUse, x, y, textBlockToUpdate, cameraId);
                }
            }
        }

        // updates temp based on mouse location
        private void UpdateTemperatureAtPosition(Mat frame, int x, int y, TextBlock textBlockToUpdate, String cameraId)
        {
            try
            {
                double pixelValue = GetPixelValue(frame, x, y, cameraId);
                Dispatcher.Invoke(() =>
                {
                    textBlockToUpdate.Text = $"Temperature: {pixelValue.ToString("F2")}°C";
                });
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error getting pixel value: {ex.Message}");
            }
        }



        // calibration stuff
        private Dictionary<string, List<CalibrationPoint>> LoadCalibrationData(string filePath)
        {
            var jsonData = File.ReadAllText(filePath);
            var cameraCalibrationDataList = JsonConvert.DeserializeObject<List<CameraCalibrationData>>(jsonData);

            return cameraCalibrationDataList.ToDictionary(data => data.CameraId, data => data.Points);
        }



        // calibration linear interp based off calibrationData.json
        private double ApplyCalibration(double cameraReading, List<CalibrationPoint> calibrationPoints)
        {
            // Convert camera reading from centiKelvin to Celsius for comparison
            double cameraReadingInCelsius = cameraReading / 100.0 - 273.15;

            // Simple linear interpolation between calibration points
            for (int i = 0; i < calibrationPoints.Count - 1; i++)
            {
                // Convert calibration points from Celsius to centiKelvin
                double calPointRawValue1 = (calibrationPoints[i].RawValue + 273.15) * 100;
                double calPointRawValue2 = (calibrationPoints[i + 1].RawValue + 273.15) * 100;

                if (cameraReadingInCelsius >= calibrationPoints[i].RawValue &&
                    cameraReadingInCelsius <= calibrationPoints[i + 1].RawValue)
                {
                    double diff = calibrationPoints[i + 1].RawValue - calibrationPoints[i].RawValue;
                    double factor = (cameraReadingInCelsius - calibrationPoints[i].RawValue) / diff;
                    return calibrationPoints[i].ReferenceTemperature +
                           factor * (calibrationPoints[i + 1].ReferenceTemperature - calibrationPoints[i].ReferenceTemperature);
                }
            }

            // If the reading is outside the calibration range, return the camera reading in Celsius
            return cameraReadingInCelsius;
        }


        // crops image to get rid of two extraneous pixel rows
        private Mat CropImage(Mat original, int width, int height)
        {
            if (original == null || original.IsEmpty)
                return new Mat();

            // Define the size and location of the crop
            Rectangle cropRect = new Rectangle(0, 0, width, height);
            Mat croppedImage = new Mat(original, cropRect);
            return croppedImage;
        }

        // calibration json logic
        public class CameraCalibrationData
        {
            public string CameraId { get; set; }
            public List<CalibrationPoint> Points { get; set; }
        }


        public class CalibrationPoint
        {
            public double RawValue { get; set; }
            public double ReferenceTemperature { get; set; }

        }
        private void OpenCalibrationFileButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start("notepad.exe", calibrationFilePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening file: {ex.Message}");
            }
        }
        private void ReloadCalibrationDataButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                calibrationData = LoadCalibrationData(calibrationFilePath);
                MessageBox.Show("Calibration data reloaded successfully.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error reloading data: {ex.Message}");
            }
        }




        // Changes raw image to bitmap for viewing
        public BitmapSource? ToBitmapSource(Mat image)
        {
            if (image == null || image.IsEmpty)
                return null;

            // Convert the Mat object to a .NET Bitmap
            Bitmap bitmap = image.ToImage<Bgr, Byte>().ToBitmap();

            // Convert the Bitmap to a BitmapSource
            BitmapSource bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                bitmap.GetHbitmap(),
                IntPtr.Zero,
                System.Windows.Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            // Important to avoid memory leak
            DeleteObject(bitmap.GetHbitmap());

            return bitmapSource;
        }

        [System.Runtime.InteropServices.DllImport("gdi32")]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}


