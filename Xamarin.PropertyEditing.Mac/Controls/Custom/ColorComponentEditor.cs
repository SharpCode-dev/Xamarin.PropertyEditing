using System;
using System.ComponentModel;
using System.Linq;
using AppKit;
using CoreAnimation;
using CoreGraphics;
using Xamarin.PropertyEditing.Drawing;
using Xamarin.PropertyEditing.ViewModels;

namespace Xamarin.PropertyEditing.Mac
{
	internal class ColorComponentEditor : ColorEditorView
	{
		private const int DefaultPropertyButtonSize = 10;
		private const int DefaultActioButtonSize = 16;
		private const int DefaultControlHeight = 22;
		private const int DefaultGradientHeight = 4;

		private ChannelEditorType EditorType { get; }

		public bool ClickableGradients { get; set; } = true;

		public ColorComponentEditor (ChannelEditorType editorType, CGRect frame) : base (frame)
		{
			EditorType = EditorType;
			Initialize ();
		}

		public ColorComponentEditor (ChannelEditorType editorType) : base ()
		{
			EditorType = editorType;
			Initialize ();
		}

		private ChannelGroup [] Editors { get; set; }
		private UnfocusableTextField hexLabel;
		private NSTextField hexEditor;

		class ChannelGroup
		{
			public UnfocusableTextField Label { get; set; }
			public ComponentSpinEditor Editor { get; set; }
			public CAGradientLayer Gradient { get; set; }
		}

		private ChannelGroup CreateEditor (ChannelEditor editor)
		{
			var ce = new ChannelGroup {
				Label = new UnfocusableTextField {
					StringValue = $"{editor.Name}:",
					Alignment = NSTextAlignment.Right,
					BackgroundColor = NSColor.Clear,
					ToolTip = editor.ToolTip
				},
				Editor = new ComponentSpinEditor (editor) {
					BackgroundColor = NSColor.Clear,
					TranslatesAutoresizingMaskIntoConstraints = true
				},
				Gradient = new UnanimatedGradientLayer {
					StartPoint = new CGPoint (0, 0),
					EndPoint = new CGPoint (1, 0),
					BorderWidth = .5f,
				}
			};

			ce.Editor.ValueChanged += UpdateComponent;
			ce.Editor.EditingEnded += UpdateComponent;
			AddSubview (ce.Label);
			AddSubview (ce.Editor);

			Layer.AddSublayer (ce.Gradient);
			return ce;
		}

		private ChannelGroup [] CreateEditors (ChannelEditorType type)
		{
			switch (type) {
				case ChannelEditorType.HSB:
					return new [] {
						CreateEditor (new HsbHueChannelEditor ()),
						CreateEditor (new HsbSaturationChannelEditor ()),
						CreateEditor (new HsbBrightnessChannelEditor ()),
						CreateEditor (new HsbAlphaChannelEditor ())
					};
				case ChannelEditorType.HLS:
					return new [] {
						CreateEditor (new HlsHueChannelEditor ()),
						CreateEditor (new HlsLightnessChannelEditor ()),
						CreateEditor (new HlsSaturationChannelEditor ()),
						CreateEditor (new HlsAlphaChannelEditor ())
					};
				case ChannelEditorType.RGB:
					return new [] {
						CreateEditor (new RedChannelEditor ()),
						CreateEditor (new GreenChannelEditor ()),
						CreateEditor (new BlueChannelEditor ()),
						CreateEditor (new AlphaChannelEditor ())
					};
				default:
				case ChannelEditorType.CMYK:
					return new [] {
						CreateEditor (new CyanChannelEditor ()),
						CreateEditor (new MagentaChannelEditor ()),
						CreateEditor (new YellowChannelEditor ()),
						CreateEditor (new BlackChannelEditor ()),
						CreateEditor (new AlphaChannelEditor ())
					};
			}
		}

		private void Initialize ()
		{
			WantsLayer = true;
			Editors = CreateEditors (EditorType);

			this.hexLabel = new UnfocusableTextField {
				StringValue = "#:",
				Alignment = NSTextAlignment.Right,
				BackgroundColor = NSColor.Clear,
				ToolTip = Properties.Resources.HexValue
			};
			AddSubview (this.hexLabel);

			this.hexEditor = new NSTextField {
				Alignment = NSTextAlignment.Right,
				BackgroundColor = NSColor.Clear
			};
			AddSubview (this.hexEditor);

			this.hexEditor.EditingEnded += (o, e) => {
				if (CommonColor.TryParseArgbHex (this.hexEditor.StringValue, out CommonColor c)) {
					ViewModel.Color = c;
					this.hexEditor.StringValue = c.ToString ();
				}
			};
		}

		public override CGSize IntrinsicContentSize => new CGSize (100, 300);

		void UpdateComponent (object sender, EventArgs args)
		{
			if (ViewModel == null)
				return;

			var color = ViewModel.Color;
			var editor = sender as ComponentSpinEditor;
			ViewModel.Color = editor.ComponentEditor.UpdateColorFromValue (color, editor.Value);
			ViewModel.CommitLastColor ();
		}

		public override void OnPropertyChanged (object sender, PropertyChangedEventArgs e)
		{
			base.OnPropertyChanged (sender, e);

			if (ViewModel == null)
				return;
			
			switch (e.PropertyName) {
				case nameof (SolidBrushViewModel.Color):
					foreach (var channelGroup in Editors) {
						var editor = channelGroup.Editor;
						editor.Value = editor.ComponentEditor.ValueFromColor (ViewModel.Color) * editor.ComponentEditor.Scale;
						editor.ComponentEditor.UpdateGradientLayer (channelGroup.Gradient, ViewModel.Color);
					}
					this.hexEditor.StringValue = ViewModel.Color.ToString ();
					break;
			}
		}

		public override void UpdateConstraints ()
		{
			base.UpdateConstraints ();
		}

		private ChannelGroup activeChannel;
		public override void MouseDown (NSEvent theEvent)
		{
			if (!ClickableGradients) {
				this.activeChannel = null;
				base.MouseDown (theEvent);
				return;
			}

			var location = ConvertPointFromView (theEvent.LocationInWindow, null);
			location = ConvertPointToLayer (location);

			foreach (var layer in Layer.Sublayers) {
				var hit = layer.PresentationLayer.HitTest (location) ?? layer.PresentationLayer.HitTest (new CGPoint (location.X, location.Y + 4));

				for (var currentLayer = hit; currentLayer != null; currentLayer = currentLayer.SuperLayer) {
					this.activeChannel = Editors.FirstOrDefault (ce => ce.Gradient == currentLayer.ModelLayer);
					if (this.activeChannel != null) {
						var channel = this.activeChannel.Editor.ComponentEditor;
						var grad = this.activeChannel.Gradient;
						ViewModel.Color = channel.UpdateColorFromLocation (
							grad,
							ViewModel.Color,
							Layer.ConvertPointToLayer (location, grad.SuperLayer));
						return;
					}
				}
			}
			base.MouseDown (theEvent);
		}

		public override void MouseDragged (NSEvent theEvent)
		{
			var location = ConvertPointFromView (theEvent.LocationInWindow, null);
			location = ConvertPointToLayer (location);

			if (this.activeChannel != null) {
				var channel = this.activeChannel.Editor.ComponentEditor;
				var grad = this.activeChannel.Gradient;
				ViewModel.Color = channel.UpdateColorFromLocation (
					grad,
					ViewModel.Color,
					Layer.ConvertPointToLayer (location, grad.SuperLayer));
				return;
			}
			base.MouseMoved (theEvent);
		}

		public override void MouseUp (NSEvent theEvent)
		{
			if (this.activeChannel != null)
				ViewModel.CommitLastColor ();

			this.activeChannel = null;
			base.MouseUp (theEvent);
		}

		public override void Layout ()
		{
			base.Layout ();

			if (Frame.IsEmpty || Frame.IsInfinite () || double.IsNaN (Frame.X) || double.IsInfinity (Frame.X))
				return;

			var frame = Bounds.Inset (padding, padding);
			var labelFrame = new CGRect (frame.X, frame.Height - DefaultControlHeight, 20, DefaultControlHeight);
			var editorFrame = new CGRect (labelFrame.Right, labelFrame.Y, frame.Width - labelFrame.Right, DefaultControlHeight);
			var yOffset = DefaultControlHeight + DefaultGradientHeight + 3;

			foreach (var channelGroup in Editors) {
				channelGroup.Label.Frame = labelFrame;
				channelGroup.Editor.Frame = editorFrame;
				channelGroup.Gradient.Frame = new CGRect (
					editorFrame.X,
					editorFrame.Y - DefaultGradientHeight + 1,
					editorFrame.Width - 16, DefaultGradientHeight);

				channelGroup.Gradient.BorderColor = NSColor.DisabledControlText.CGColor;
				channelGroup.Gradient.ContentsScale = Window?.Screen?.BackingScaleFactor ?? NSScreen.MainScreen.BackingScaleFactor;
				labelFrame = labelFrame.Translate (0, -yOffset);
				editorFrame = editorFrame.Translate (0, -yOffset);
			}

			this.hexLabel.Frame = new CGRect (frame.X, padding, 20, DefaultControlHeight);
			this.hexEditor.Frame = new CGRect (
				labelFrame.Right,
				padding,
				frame.Width - labelFrame.Right - 16,
				DefaultControlHeight);
		}
	}
}
