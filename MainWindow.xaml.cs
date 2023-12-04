using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Forms;
using System.Diagnostics;
using Emgu.CV;
using System.Drawing;
using PixelFormat = System.Drawing.Imaging.PixelFormat;
using Emgu.CV.Structure;
using System.Windows.Interop;
using Point = System.Drawing.Point;
using System.Windows.Input;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Image = System.Windows.Controls.Image;
using System.Threading.Tasks;
using System.Windows.Threading;
using Emgu.CV.CvEnum;
using System.Runtime.InteropServices;
using System.IO;
using MessageBox = System.Windows.MessageBox;


namespace thermalCamera
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
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



        public MainWindow()
        {
            InitializeComponent();
            PopulateCameraSelection();
            recordButton.IsEnabled = false; // Disable record button initially
            directoryMessageTextBlock.Visibility = selectedFolderPath == "" ? Visibility.Visible : Visibility.Collapsed;
        }
        private void PopulateCameraSelection()
        {
            int index = 0;
            while (true)
            {
                using (var tempCapture = new VideoCapture(index))
                {
                    CheckCameraPixelFormat(tempCapture);
                    if (tempCapture.IsOpened)
                    {
                        camera1Selector.Items.Add("Camera " + index);
                        camera2Selector.Items.Add("Camera " + index);
                        index++;
                    }
                    else
                    {
                        break; // No more cameras
                    }
                }
            }

            camera1Selector.SelectionChanged += CameraSelectionChanged;
            camera2Selector.SelectionChanged += CameraSelectionChanged;
        }

        private void CameraSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            startStopButton.IsEnabled = camera1Selector.SelectedIndex != -1 || camera2Selector.SelectedIndex != -1;
        }


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
                }

                // Initialize and start the second camera
                if (camera2Index >= 0)
                {
                    camera2Capture = new VideoCapture(camera2Index, VideoCapture.API.DShow);
                    SetCameraToY16(camera2Capture);
                    camera2Capture.ImageGrabbed += ProcessFrameCamera2;
                    camera2Capture.Start();
                }
            });
        }
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
                Trace.WriteLine($"Set FOURCC: {fourcc}, Read FOURCC: {readFourcc}");
                Trace.WriteLine($"Set ConvertRgb: 0, Read ConvertRgb: {readConvertRgb}");
            }
        }

        private Mat? CaptureFrameFromCamera(VideoCapture camera)
        {
            if (camera != null && camera.IsOpened)
            {
                Mat frame = new Mat();
                if (camera.Read(frame))
                {
                    return frame;
                }
            }
            return null;
        }
        private void CheckCameraPixelFormat(VideoCapture camera)
        {
            Mat frame = CaptureFrameFromCamera(camera)!;
            if (frame != null)
            {
                DepthType depth = frame.Depth;
                int numberOfChannels = frame.NumberOfChannels;

                Trace.WriteLine($"Frame Depth: {depth}, Channels: {numberOfChannels}");

                // Checking if it's a 16-bit single-channel image (Y16)
                if (depth == DepthType.Cv16U && numberOfChannels == 1)
                {
                    Trace.WriteLine("The camera is providing Y16 format images.");
                }
                else
                {
                    Trace.WriteLine("The camera is not providing Y16 format images.");
                }
            }
            else
            {
                Trace.WriteLine("Failed to capture a frame from the camera.");
            }
        }

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
        private void ProcessFrameCamera1(object sender, EventArgs e)
        {
            if (camera1Capture != null && camera1Capture.Ptr != IntPtr.Zero)
            {
                if (latestFrameCamera1 == null)
                    latestFrameCamera1 = new Mat();

                camera1Capture.Retrieve(latestFrameCamera1);

                if (isRecording)
                {
                    string fileName = $"camera1_{camera1FileCounter:D4}.tiff";
                    string filePath = Path.Combine(recordingFolderPath, fileName);
                    camera1FileCounter++;
                    latestFrameCamera1.Save(filePath); // Save the raw Y16 frame
                }

                // Convert the Y16 frame for display
                Mat displayableFrame = ConvertY16ToDisplayableRgb(latestFrameCamera1);

                Dispatcher.Invoke(() =>
                {
                    // Update the image source
                    camera1Image.Source = ToBitmapSource(displayableFrame);
                });
            }
        }

        // Similar changes for ProcessFrameCamera2




        private void ProcessFrameCamera2(object sender, EventArgs e)
        {
            if (camera2Capture != null && camera2Capture.Ptr != IntPtr.Zero)
            {
                if (latestFrameCamera2 == null)
                    latestFrameCamera2 = new Mat();

                camera2Capture.Retrieve(latestFrameCamera2);

                if (isRecording)
                {
                    string fileName = $"camera2_{camera1FileCounter:D4}.tiff";
                    string filePath = Path.Combine(recordingFolderPath, fileName);
                    camera2FileCounter++;
                    latestFrameCamera2.Save(filePath); // Save the raw Y16 frame
                }

                // Convert the Y16 frame for display
                Mat displayableFrame = ConvertY16ToDisplayableRgb(latestFrameCamera2);

                Dispatcher.Invoke(() =>
                {
                    // Update the image source
                    camera2Image.Source = ToBitmapSource(displayableFrame);
                });
            }
        }
        private Mat ConvertY16ToDisplayableRgb(Mat y16Image)
        {
            if (y16Image == null || y16Image.IsEmpty)
                return new Mat();

            // Ensure the image is 16-bit single-channel
            if (y16Image.Depth != DepthType.Cv16U || y16Image.NumberOfChannels != 1)
            {
                throw new InvalidOperationException("Expected Y16 format (16-bit single-channel)");
            }

            // Normalize the 16-bit image to 8-bit for display
            Mat normalizedImage = new Mat();
            CvInvoke.Normalize(y16Image, normalizedImage, 0, 255, NormType.MinMax, DepthType.Cv8U);
            Mat rgbImage = new Mat();
            CvInvoke.CvtColor(normalizedImage, rgbImage, ColorConversion.Gray2Bgr);
            return rgbImage;
        }

        private double GetPixelValue(Mat frame, int x, int y)
        {
            if (frame == null || frame.IsEmpty || x < 0 || y < 0 || x >= frame.Cols || y >= frame.Rows)
            {
                throw new ArgumentException("Invalid frame or pixel coordinates.");
            }

            // Assuming the frame is a single channel grayscale image
            var value = Marshal.ReadByte(frame.DataPointer + y * frame.Step + x * frame.ElementSize);
            return value/100 + 273.15;
        }




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

                // Ensure the recordingFolderPath is always based on the selectedFolderPath
                recordingFolderPath = Path.Combine(selectedFolderPath, CreateRecordingFolderName(selectedOption));
                camera1FileCounter = 0;
                camera2FileCounter = 0;
            }
            else
            {
                // Stop recording logic if needed
            }
        }
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
        private void QuitButton_Click(object sender, RoutedEventArgs e)
        {
            ReleaseCameraResources();
            System.Windows.Application.Current.Shutdown();
        }
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

        private bool IsCameraSendingFrames(VideoCapture camera)
        {
            if (camera == null)
                return false;

            try
            {
                using (Mat frame = new Mat())
                {
                    camera.Read(frame);
                    if (!frame.IsEmpty)
                        return true;
                }
            }
            catch
            {
                // Handle exceptions if necessary
            }

            camera.Dispose();
            return false;
        }
        private void SaveRawFrame(Mat frame, string filePath)
        {
            try
            {
                frame.Save(filePath); // Emgu.CV's method to save Mat object
            }
            catch (Exception ex)
            {
                // Handle exceptions, like issues with file writing
                Debug.WriteLine("Error saving frame: " + ex.Message);
            }
        }

        
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


