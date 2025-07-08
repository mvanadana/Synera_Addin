        using Synera.Core.Benchmarking;
        using Synera.Core.Graph.Data;
        using Synera.Core.Graph.Enums;
using Synera.Core.Implementation.ApplicationService.Settings.CustomProperties;
using Synera.Core.Implementation.Graph;
        using Synera.Core.Viewer;
        using Synera.DataTypes;
        using Synera.Kernels.DataTypes;
using Synera.Kernels.Geometry;
using Synera.Localization;
        using Synera.Wpf.Common.Controls.Drawable;
        using Synera.Wpf.Common.Interactions;
        using System;
        using System.Drawing;
        using System.IO;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using System.Windows;
        using System.Windows.Controls;
        using System.Windows.Media;

        namespace Synera_Addin
        {
            [System.Runtime.InteropServices.Guid("F4468A42-7424-4B7C-A5A4-6949183E12BD")]
            public sealed class HelloWorld : Node, IViewer3DViewModel
            {
                private string _fileContent = string.Empty;
                private byte[] _fileBytes;

        public object Content => throw new NotImplementedException();

                public HelloWorld() : base(new LocalizableString(nameof(Resources.SmileyFaceContainerName), typeof(Resources)))
                {
        
                    Category = Synera.Core.Implementation.UI.Categories.Data;
                    Subcategory = Synera.Core.Implementation.UI.Subcategories.Data.Control;
                    Description = "A node that outputs a custom Hello World! message and renders file content in the drawing area.";
                    GuiPriority = 2;
                    
                    InputParameterManager.AddParameter<SyneraString>("File Path", "Path to the input file.");
                    InputParameterManager.AddParameter<SyneraString>("Message", "The message to be added after the Hello, World! message.");
                    InputParameterManager.AddParameter<SyneraBool>("New line", "If true, a newline will be added between the message and Hello, World!.");
                    InputParameterManager.AddParameter<SyneraInt>(
                        new LocalizableString(nameof(Resources.SmileyFaceContainerName), typeof(Resources)),
                        new LocalizableString(nameof(Resources.SmileyFaceContainerDescription), typeof(Resources)),
                        ParameterAccess.Item);

                    OutputParameterManager.AddParameter<SyneraString>("Text", "The custom Hello, World! message.");
                }

        protected override void SolveInstance(IDataAccess dataAccess)
        {
            bool isDataSuccess = dataAccess.GetData(0, out SyneraString filePath);
            isDataSuccess &= dataAccess.GetData(1, out SyneraString message);
            isDataSuccess &= dataAccess.GetData(2, out SyneraBool newline);
            isDataSuccess &= dataAccess.GetData(3, out SyneraInt smileyCount);

            if (!isDataSuccess)
                return;

            try
            {
                if (!File.Exists(filePath))
                {
                    _fileContent = $"File not found: {filePath}";
                }
                else
                {
                    _fileContent = File.ReadAllText(filePath);
                    _fileBytes = File.ReadAllBytes(filePath);
                }
            }
            catch (Exception ex)
            {
                _fileContent = $"Error reading file: {ex.Message}";
            }

            var separator = newline ? Environment.NewLine : " ";
            var smileys = new string('k', smileyCount.Value);
            var newString = _fileBytes.ToString();

            dataAccess.SetData(0, newString);
        }


        public void Draw(DrawingContext drawingContext, System.Windows.Size size)
        {
            // Background
            drawingContext.DrawRectangle(Brushes.White, null, new Rect(0, 0, size.Width, size.Height));

            // Draw file content as text
            if (!string.IsNullOrEmpty(_fileContent))
            {
                var formattedText = new FormattedText(
                    _fileContent,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"),
                    14,
                    Brushes.Black,
                    VisualTreeHelper.GetDpi(Application.Current.MainWindow).PixelsPerDip // Use Application.Current.MainWindow for DPI
                );

                // Clip text to drawing area
                var textRect = new Rect(10, 10, size.Width - 20, size.Height - 20);
                drawingContext.PushClip(new RectangleGeometry(textRect));
                drawingContext.DrawText(formattedText, new System.Windows.Point(10, 10));
                drawingContext.Pop();
            }
            else
            {
                var formattedText = new FormattedText(
                    "No file content to display.",
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"),
                    14,
                    Brushes.Gray,
                    VisualTreeHelper.GetDpi(Application.Current.MainWindow).PixelsPerDip // Use Application.Current.MainWindow for DPI
                );
                drawingContext.DrawText(formattedText, new System.Windows.Point(10, 10));
            }
        }

                public void Dispose()
                {
                    throw new NotImplementedException();
                }

                public Task<IBenchmarkReport> BenchmarkAsync(CancellationToken cancellationToken)
                {
                    throw new NotImplementedException();
                }
            }
        }
