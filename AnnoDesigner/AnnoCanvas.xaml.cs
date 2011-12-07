﻿using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace AnnoDesigner
{
    /// <summary>
    /// Interaction logic for AnnoCanvas.xaml
    /// </summary>
    public partial class AnnoCanvas
        : UserControl
    {
        #region Properties

        private int _gridStep = 20;
        public int GridSize
        {
            get
            {
                return _gridStep;
            }
            set
            {
                if (_gridStep != value)
                {
                    InvalidateVisual();
                }
                _gridStep = value;
            }
        }

        private bool _renderGrid;
        public bool RenderGrid
        {
            get
            {
                return _renderGrid;
            }
            set
            {
                if (_renderGrid != value)
                {
                    InvalidateVisual();
                }
                _renderGrid = value;
            }
        }

        private bool _renderLabel;
        public bool RenderLabel
        {
            get
            {
                return _renderLabel;
            }
            set
            {
                if (_renderLabel != value)
                {
                    InvalidateVisual();
                }
                _renderLabel = value;
            }
        }

        private bool _renderIcon;
        public bool RenderIcon
        {
            get
            {
                return _renderIcon;
            }
            set
            {
                if (_renderIcon != value)
                {
                    InvalidateVisual();
                }
                _renderIcon = value;
            }
        }

        #endregion

        private Point _mousePosition;
        private bool _mouseWithinControl;
        
        private List<AnnoObject> _placedObjects;
        private readonly List<AnnoObject> _selectedObjects; 
        private AnnoObject _currentObject;

        private readonly Pen _linePen;
        private readonly Pen _highlightPen;

        public AnnoCanvas()
        {
            InitializeComponent();
            _placedObjects = new List<AnnoObject>();
            _selectedObjects = new List<AnnoObject>();
            _linePen = new Pen(Brushes.Black, 1);
            _highlightPen = new Pen(Brushes.Yellow, 1);
        }

        #region Rendering

        protected override void OnRender(DrawingContext drawingContext)
        {
            var m = PresentationSource.FromVisual(this).CompositionTarget.TransformToDevice;
            var dpiFactor = 1 / m.M11;
            _linePen.Thickness = dpiFactor * 1;
            _highlightPen.Thickness = dpiFactor * 2;

            // assure pixel perfect drawing
            //BUG: doesn't work when exporting
            var halfPenWidth = _linePen.Thickness / 2;
            var guidelines = new GuidelineSet();
            guidelines.GuidelinesX.Add(halfPenWidth);
            guidelines.GuidelinesY.Add(halfPenWidth);
            drawingContext.PushGuidelineSet(guidelines);

            var width = RenderSize.Width;
            var height = RenderSize.Height;

            // draw background
            drawingContext.DrawRectangle(Brushes.White, null, new Rect(new Point(), RenderSize));

            // draw grid
            if (RenderGrid)
            {
                for (var i = 0; i < width; i += _gridStep)
                {
                    drawingContext.DrawLine(_linePen, new Point(i, 0), new Point(i, height));
                }
                for (var i = 0; i < height; i += _gridStep)
                {
                    drawingContext.DrawLine(_linePen, new Point(0, i), new Point(width, i));
                }
            }

            // draw mouse grid position highlight
            //drawingContext.DrawRectangle(Brushes.LightYellow, linePen, new Rect(GridToScreen(ScreenToGrid(_mousePosition)), new Size(_gridStep, _gridStep)));

            // draw placed objects
            foreach (var obj in _placedObjects)
            {
                RenderObject(drawingContext, obj, _selectedObjects.Contains(obj) ? _highlightPen : _linePen);
            }

            if (_currentObject == null)
            {
                // highlight object which is currently hovered
                var hoveredObj = GetObjectAt(_mousePosition);
                if (hoveredObj != null)
                {
                    drawingContext.DrawRectangle(null, _highlightPen, GetObjectScreenRect(hoveredObj));
                }
            }
            else
            {
                // draw current object
                if (_mouseWithinControl)
                {
                    RepositionCurrentObject();
                    // draw influence radius
                    RenderObjectInfluence(drawingContext, _currentObject);
                    // draw with transparency
                    _currentObject.Color.A = 128;
                    RenderObject(drawingContext, _currentObject, _linePen);
                    _currentObject.Color.A = 255;
                }
            }

            // pop back guidlines set
            drawingContext.Pop();
        }

        private void RepositionCurrentObject()
        {
            // determine grid position beneath mouse
            var pos = _mousePosition;
            var size = GridToScreen(_currentObject.Size);
            pos.X -= size.Width / 2;
            pos.Y -= size.Height / 2;
            _currentObject.Position = RoundScreenToGrid(pos);
        }

        private void RenderObject(DrawingContext drawingContext, AnnoObject obj, Pen pen)
        {
            // draw object rectangle
            var objRect = GetObjectScreenRect(obj);
            drawingContext.DrawRectangle(new SolidColorBrush(obj.Color), pen, objRect);
            // draw object icon if it is at least 2x2 cells
            if (_renderIcon && !string.IsNullOrEmpty(obj.Icon) && obj.Size.Width > 1 && obj.Size.Height > 1)
            {
                // draw icon 2x2 grid cells large
                var iconSize = GridToScreen(new Size(2,2));
                // center icon within the object
                var iconPos = objRect.TopLeft;
                iconPos.X += objRect.Width/2 - iconSize.Width/2;
                iconPos.Y += objRect.Height/2 - iconSize.Height/2;
                drawingContext.DrawImage(new BitmapImage(new Uri(obj.Icon, UriKind.Relative)), new Rect(iconPos, iconSize));
            }
            // draw object label
            if (_renderLabel)
            {
                var textPoint = objRect.TopLeft;
                textPoint.Y += objRect.Height / 2;
                var text = new FormattedText(obj.Label, Thread.CurrentThread.CurrentCulture, FlowDirection.LeftToRight,
                                             new Typeface("Verdana"), 12, Brushes.Black)
                {
                    TextAlignment = TextAlignment.Center,
                    MaxTextWidth = objRect.Width,
                    MaxTextHeight = objRect.Height
                };
                textPoint.Y -= text.Height / 2;
                drawingContext.DrawText(text, textPoint);
            }
        }

        private void RenderObjectInfluence(DrawingContext drawingContext, AnnoObject obj)
        {
            var radius = GridToScreen(obj.Radius);
            var color = Colors.LightYellow;
            color.A = 128;
            drawingContext.DrawEllipse(new SolidColorBrush(color), _linePen, GetCenterPoint(GetObjectScreenRect(obj)), radius, radius);
        }

        #endregion

        #region Coordinate and rectangle conversions

        /// <summary>
        /// Convert a screen coordinate to a grid coordinate by determining in which grid cell the point is contained.
        /// </summary>
        /// <param name="screenPoint"></param>
        /// <returns></returns>
        [Pure]
        private Point ScreenToGrid(Point screenPoint)
        {
            return new Point(Math.Floor(screenPoint.X / _gridStep), Math.Floor(screenPoint.Y / _gridStep));
        }

        /// <summary>
        /// Converts a screen coordinate to a grid coordinate by determining which grid cell is nearest.
        /// </summary>
        /// <param name="screenPoint"></param>
        /// <returns></returns>
        [Pure]
        private Point RoundScreenToGrid(Point screenPoint)
        {
            return new Point(Math.Round(screenPoint.X / _gridStep), Math.Round(screenPoint.Y / _gridStep));
        }

        /// <summary>
        /// Convert a grid coordinate to a screen coordinate.
        /// </summary>
        /// <param name="gridPoint"></param>
        /// <returns></returns>
        [Pure]
        private Point GridToScreen(Point gridPoint)
        {
            return new Point(gridPoint.X * _gridStep, gridPoint.Y * _gridStep);
        }

        /// <summary>
        /// Converts a size given in grid cells to a size given in (pixel-)units.
        /// </summary>
        /// <param name="gridSize"></param>
        /// <returns></returns>
        [Pure]
        private Size GridToScreen(Size gridSize)
        {
            return new Size(gridSize.Width * _gridStep, gridSize.Height * _gridStep);
        }

        /// <summary>
        /// Converts a length given in grid cells to a size given in (pixel-)units.
        /// </summary>
        /// <param name="gridLength"></param>
        /// <returns></returns>
        [Pure]
        private double GridToScreen(double gridLength)
        {
            return gridLength * _gridStep;
        }

        /// <summary>
        /// Calculates the exact center point of a given rect
        /// </summary>
        /// <param name="rect"></param>
        /// <returns></returns>
        [Pure]
        private static Point GetCenterPoint(Rect rect)
        {
            var pos = rect.Location;
            var size = rect.Size;
            pos.X += size.Width / 2;
            pos.Y += size.Height / 2;
            return pos;
        }

        /// <summary>
        /// Generates the rect to which the given object is rendered.
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        [Pure]
        private Rect GetObjectScreenRect(AnnoObject obj)
        {
            return new Rect(GridToScreen(obj.Position), GridToScreen(obj.Size));
        }

        /// <summary>
        /// Gets the rect which is used for collision detection for the given object.
        /// Prevents undesired collisions which occur when using GetObjectScreenRect().
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        [Pure]
        private static Rect GetObjectCollisionRect(AnnoObject obj)
        {
            return new Rect(obj.Position, new Size(obj.Size.Width - 0.5, obj.Size.Height - 0.5));
        }

        /// <summary>
        /// Rotates the given Size object, i.e. switches width and height.
        /// </summary>
        /// <param name="size"></param>
        /// <returns></returns>
        [Pure]
        private static Size Rotate(Size size)
        {
            return new Size(size.Height, size.Width);
        }

        #endregion

        #region Event handling

        protected override void OnMouseEnter(MouseEventArgs e)
        {
            _mouseWithinControl = true;
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            _mouseWithinControl = false;
            InvalidateVisual();
        }

        private void HandleMouseClick(MouseEventArgs e)
        {
            // refresh retrieved mouse position
            _mousePosition = e.GetPosition(this);
            // place new object
            if (e.LeftButton == MouseButtonState.Pressed && _currentObject != null)
            {
                RepositionCurrentObject();
                TryPlaceCurrentObject();
            }
            // remove clicked object
            if (e.RightButton == MouseButtonState.Pressed && _currentObject == null)
            {
                _placedObjects.Remove(GetObjectAt(_mousePosition));
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            HandleMouseClick(e);
            InvalidateVisual();
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            HandleMouseClick(e);
            // select object
            if (e.LeftButton == MouseButtonState.Pressed && _currentObject == null)
            {
                
                if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    _selectedObjects.Clear();
                }
                var obj = GetObjectAt(_mousePosition);
                if (obj != null)
                {
                    if (_selectedObjects.Contains(obj))
                    {
                        _selectedObjects.Remove(obj);
                    }
                    else
                    {
                        _selectedObjects.Add(obj);   
                    }
                }
            }
            // cancel placement of object
            if (e.RightButton == MouseButtonState.Pressed && _currentObject != null)
            {
                _currentObject = null;
            }
            // rotate current object
            if (e.MiddleButton == MouseButtonState.Pressed && _currentObject != null)
            {
                _currentObject.Size = Rotate(_currentObject.Size);
            }
            InvalidateVisual();
        }

        #endregion

        #region Collision handling

        private bool IntersectionExists(AnnoObject a, AnnoObject b)
        {
            return GetObjectCollisionRect(a).IntersectsWith(GetObjectCollisionRect(b));
        }

        private bool IntersectionTest(AnnoObject obj)
        {
            return _placedObjects.Exists(_ => IntersectionExists(obj, _));
        }

        private bool TryPlaceCurrentObject()
        {
            if (_currentObject != null && !IntersectionTest(_currentObject))
            {
                _placedObjects.Add(new AnnoObject(_currentObject));
                return true;
            }
            return false;
        }

        private AnnoObject GetObjectAt(Point position)
        {
            return _placedObjects.FindLast(_ => GetObjectScreenRect(_).Contains(position));
        }

        #endregion

        #region API

        public void SetCurrentObject(AnnoObject obj)
        {
            obj.Position = _mousePosition;
            _currentObject = obj;
            _selectedObjects.Clear();
            InvalidateVisual();
        }

        public void ClearPlacedObjects()
        {
            _placedObjects.Clear();
            InvalidateVisual();
        }

        #endregion

        #region Save/Load/Export methods

        public void SaveToFile()
        {
            var dialog = new SaveFileDialog
            {
                DefaultExt = ".ad",
                Filter = "Anno Designer Files (*.ad)|*.ad|All Files (*.*)|*.*"
            };
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    DataIO.SaveToFile(_placedObjects, dialog.FileName);
                }
                catch (Exception)
                {
                    IOErrorMessageBox();
                }
            }
        }

        public void OpenFile()
        {
            var dialog = new OpenFileDialog
            {
                DefaultExt = ".ad",
                Filter = "Anno Designer Files (*.ad)|*.ad|All Files (*.*)|*.*"
            };
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    DataIO.LoadFromFile(out _placedObjects, dialog.FileName);
                }
                catch (Exception)
                {
                    IOErrorMessageBox();
                }
            }
        }

        public void ExportImage()
        {
            var dialog = new SaveFileDialog
            {
                DefaultExt = ".png",
                Filter = "PNG (*.png)|*.png|All Files (*.*)|*.*"
            };
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    DataIO.RenderToFile(this, dialog.FileName);
                }
                catch (Exception)
                {
                    IOErrorMessageBox();
                }
            }
        }

        private void IOErrorMessageBox()
        {
            MessageBox.Show("Something went wrong while saving/loading file.");
        }

        #endregion
    }
}