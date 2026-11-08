#!/usr/bin/env python3
"""WPF XAML -> Avalonia XAML converter used to port DashX360 to Linux.

The output targets the runtime compatibility layer in Compat/ (namespace prefix `w:`), which
implements WPF Triggers/DataTriggers/Storyboards on top of Avalonia.

Usage: wpf2avalonia.py <input.xaml> <output.axaml> [--report report.txt]
"""
import math
import re
import sys
from xml.dom import minidom, Node

AVALONIA_NS = "https://github.com/avaloniaui"
COMPAT_NS = "using:XboxMetroLauncher.Compat"
SELAWIK = "avares://DashX360/Fonts#Selawik"
ICON_FONTS = ["avares://DashX360/Fonts#Noto Sans Symbols 2", "DejaVu Sans"]
import os as _os
exec(open(_os.path.join(_os.path.dirname(_os.path.abspath(__file__)), "mdl2_map.py")).read())


def map_icon_glyphs(text):
    """Replace Segoe MDL2 private-use glyphs with free Unicode symbols."""
    return "".join(chr(MDL2_MAP.get(ord(ch), ord(ch))) for ch in text)

# WPF-only base classes used as owners in property element / setter names.
TRANSPARENT_OWNERS = {
    "UIElement", "FrameworkElement", "Control", "ContentControl", "ButtonBase", "Selector", "Shape", "Panel",
    "Visual", "Animatable", "TextBoxBase", "RangeBase", "ToggleButton", "ItemsControl", "Decorator", "HeaderedContentControl",
    "Border", "TextBlock", "Grid", "Canvas", "Button", "Image", "StackPanel", "Window", "UserControl", "ListBox", "ListBoxItem",
    "ComboBox", "ComboBoxItem", "ScrollViewer", "Slider", "TextBox", "CheckBox", "ContentPresenter", "Rectangle", "Ellipse", "Path",
    "Viewbox", "ProgressBar", "DockPanel", "WrapPanel", "UniformGrid", "TriggerBase", "Popup", "Line", "Run",
}
WPF_STRETCH_DEFAULTS = {"Button", "ToggleButton", "RepeatButton", "ComboBox", "TextBox", "Slider", "ListBox", "ItemsControl", "ScrollViewer", "ProgressBar"}
# Controls with a Fluent ControlTheme that can be used as BasedOn.
TEMPLATED_CONTROLS = {
    "Button", "ToggleButton", "CheckBox", "RadioButton", "ComboBox", "ComboBoxItem", "ListBox", "ListBoxItem", "TextBox",
    "Slider", "ScrollViewer", "ProgressBar", "ItemsControl", "RepeatButton",
    "ScrollBar", "Expander", "TabControl", "TabItem", "MenuItem", "ToolTip", "Label",
}
CONTENT_TEMPLATE = ('<ControlTemplate><ContentPresenter Name="PART_ContentPresenter" Content="{TemplateBinding Content}" '
                    'ContentTemplate="{TemplateBinding ContentTemplate}" Background="{TemplateBinding Background}" '
                    'BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" '
                    'Padding="{TemplateBinding Padding}" HorizontalContentAlignment="{TemplateBinding HorizontalContentAlignment}" '
                    'VerticalContentAlignment="{TemplateBinding VerticalContentAlignment}"/></ControlTemplate>')
# Element-level attributes with no Avalonia equivalent.
DROP_ATTRS = {
    "SnapsToDevicePixels", "TextOptions.TextFormattingMode", "TextOptions.TextRenderingMode", "TextOptions.TextHintingMode",
    "FocusVisualStyle", "FrameworkElement.FocusVisualStyle", "OverridesDefaultStyle", "RecognizesAccessKey",
    "KeyboardNavigation.DirectionalNavigation", "KeyboardNavigation.ControlTabNavigation", "FocusManager.IsFocusScope",
    "ScrollViewer.CanContentScroll", "ScrollViewer.PanningMode", "VirtualizingPanel.IsVirtualizing", "VirtualizingPanel.VirtualizationMode",
    "VirtualizingStackPanel.IsVirtualizing", "VirtualizingStackPanel.VirtualizationMode", "VirtualizingPanel.ScrollUnit",
    "Stylus.IsFlicksEnabled", "Stylus.IsPressAndHoldEnabled", "AllowsTransparency", "ResizeMode", "WindowStyle", "AllowDrop",
    "IsMoveToPointEnabled", "RenderingType", "TextBlock.LineStackingStrategy", "LineStackingStrategy", "ForceCursor",
    "UIElement.SnapsToDevicePixels", "IsSynchronizedWithCurrentItem", "ScrollViewer.IsDeferredScrollingEnabled",
    "AutomationProperties.Name", "KeyboardNavigation.AcceptsReturn", "Validation.ErrorTemplate", "BitmapEffect", "CacheMode",
    "TextOptions.TextRendering", "SelectionOpacity", "IsInactiveSelectionHighlightEnabled", "AutoWordSelection",
    "FocusManager.FocusedElement", "VerticalScrollBarVisibility.Disabled",
}
EVENT_RENAMES = {
    "MouseEnter": "PointerEntered",
    "MouseLeave": "PointerExited",
    "MouseLeftButtonDown": "PointerPressed",
    "MouseDown": "PointerPressed",
    "MouseLeftButtonUp": "PointerReleased",
    "MouseUp": "PointerReleased",
    "MouseMove": "PointerMoved",
    "MouseWheel": "PointerWheelChanged",
    "GotKeyboardFocus": "GotFocus",
    "LostKeyboardFocus": "LostFocus",
    "TextInput": "TextInput",
}
TUNNEL_EVENTS = {
    "PreviewKeyDown": ("KeyDownEvent", "KeyEventArgs"),
    "PreviewKeyUp": ("KeyUpEvent", "KeyEventArgs"),
    "PreviewMouseDown": ("PointerPressedEvent", "PointerPressedEventArgs"),
    "PreviewMouseLeftButtonDown": ("PointerPressedEvent", "PointerPressedEventArgs"),
    "PreviewMouseUp": ("PointerReleasedEvent", "PointerReleasedEventArgs"),
    "PreviewMouseLeftButtonUp": ("PointerReleasedEvent", "PointerReleasedEventArgs"),
    "PreviewMouseMove": ("PointerMovedEvent", "PointerEventArgs"),
    "PreviewMouseWheel": ("PointerWheelChangedEvent", "PointerWheelEventArgs"),
    "PreviewTextInput": ("TextInputEvent", "TextInputEventArgs"),
}
# Avalonia handles TextInput as a tunnel-able routed event; WPF TextInput bubbles.
EVENT_NAMES = set(EVENT_RENAMES) | set(TUNNEL_EVENTS) | {
    "Click", "Loaded", "Unloaded", "KeyDown", "KeyUp", "SelectionChanged", "TextChanged", "Checked", "Unchecked", "ValueChanged",
    "SizeChanged", "Closing", "Closed", "Activated", "Deactivated", "GotFocus", "LostFocus", "DropDownOpened", "DropDownClosed",
    "ScrollChanged", "ContentRendered", "IsVisibleChanged", "MouseRightButtonUp", "MouseRightButtonDown", "StateChanged",
}

report = []


def warn(msg):
    report.append(msg)


class Ctx:
    def __init__(self):
        self.in_template = False  # inside ControlTemplate (TemplateBinding etc.)
        self.in_trigger = False
        self.element_events = []  # (element name, event, handler)
        self.auto_name = 0


def local(tag):
    return tag.split(":", 1)[1] if ":" in tag else tag


def prefix(tag):
    return tag.split(":", 1)[0] if ":" in tag else ""


def children(el):
    return [c for c in el.childNodes if c.nodeType == Node.ELEMENT_NODE]


def rel_point(value):
    v = value.strip()
    if "%" in v or v.startswith("{"):
        return v
    parts = [p.strip() for p in re.split(r"[ ,]+", v) if p.strip()]
    if len(parts) != 2:
        return v
    return ",".join(fmt_num(float(p) * 100) + "%" for p in parts)


def fmt_num(n):
    if abs(n - round(n)) < 1e-9:
        return str(int(round(n)))
    return ("%.6f" % n).rstrip("0").rstrip(".")


def rel_scalar(value):
    v = value.strip()
    if "%" in v or v.startswith("{"):
        return v
    try:
        return fmt_num(float(v) * 100) + "%"
    except ValueError:
        return v


def fix_font_family(value):
    if value.startswith("{"):
        return value
    parts = [p.strip() for p in value.split(",")]
    out = []
    for p in parts:
        if p.lower() in ("segoe ui", "segoe ui light", "segoe ui semibold", "segoe ui semilight", "segoe ui variable", "selawik"):
            if SELAWIK not in out:
                out.append(SELAWIK)
        elif p.lower() in ("segoe mdl2 assets", "segoe fluent icons", "segoe ui symbol", "segoe ui emoji"):
            for icon_font in ICON_FONTS:
                if icon_font not in out:
                    out.append(icon_font)
        else:
            out.append(p)
    if SELAWIK not in out:
        out.append(SELAWIK)
    return ", ".join(out)


def convert_markup(value, ctx, prop=None):
    """Rewrite markup-extension strings ({Binding}, {TemplateBinding}, {x:Type})."""
    if not value.startswith("{"):
        return value
    v = value
    # TemplateBinding Owner.Prop -> TemplateBinding Prop
    v = re.sub(r"\{TemplateBinding\s+(?:[A-Za-z]+\.)?([A-Za-z]+)\s*\}", lambda m: "{TemplateBinding %s}" % map_prop_name(m.group(1)), v)
    v = re.sub(r"\{TemplateBinding\s+Property=(?:[A-Za-z]+\.)?([A-Za-z]+)\s*\}", lambda m: "{TemplateBinding %s}" % map_prop_name(m.group(1)), v)
    # Binding cleanups
    v = re.sub(r",\s*NotifyOnSourceUpdated=\w+", "", v)
    v = re.sub(r",\s*NotifyOnTargetUpdated=\w+", "", v)
    v = re.sub(r",\s*IsAsync=\w+", "", v)
    v = re.sub(r",\s*ValidatesOnDataErrors=\w+", "", v)
    v = re.sub(r",\s*ValidatesOnExceptions=\w+", "", v)
    v = re.sub(r",\s*UpdateSourceTrigger=Default", "", v)
    v = v.replace("{x:Type ", "{x:Type ")
    # RelativeSource FindAncestor syntax
    v = re.sub(r"\{RelativeSource\s+FindAncestor\s*,\s*AncestorType=", "{RelativeSource AncestorType=", v)
    v = re.sub(r"\{RelativeSource\s+Mode=FindAncestor\s*,\s*AncestorType=", "{RelativeSource AncestorType=", v)
    v = re.sub(r"AncestorType=\{x:Type\s+([\w:]+)\}", r"AncestorType=\1", v)
    # Avalonia binding property paths: (Owner.Prop) for WPF-only owners
    v = re.sub(r"Path=\((?:UIElement|FrameworkElement)\.(\w+)\)", lambda m: "Path=%s" % map_prop_name(m.group(1)), v)
    v = re.sub(r"\{Binding\s+\((?:UIElement|FrameworkElement)\.(\w+)\)", lambda m: "{Binding %s" % map_prop_name(m.group(1)), v)
    v = re.sub(r"Path=IsMouseOver\b", "Path=IsPointerOver", v)
    v = re.sub(r"Path=IsKeyboardFocused\b", "Path=IsFocused", v)
    v = re.sub(r"Path=ActualWidth\b", "Path=Bounds.Width", v)
    v = re.sub(r"Path=ActualHeight\b", "Path=Bounds.Height", v)
    v = re.sub(r"\{Binding\s+ActualWidth\b", "{Binding Bounds.Width", v)
    v = re.sub(r"\{Binding\s+ActualHeight\b", "{Binding Bounds.Height", v)
    v = re.sub(r"Path=Visibility\b", "Path=(w:Wpf.Visibility)", v)
    if ctx.in_trigger and ctx.in_template:
        v = v.replace("{RelativeSource TemplatedParent}", "{RelativeSource Self}")
        v = v.replace("{RelativeSource Mode=TemplatedParent}", "{RelativeSource Self}")
    if v.startswith("{DynamicResource ") and ctx.in_trigger:
        v = "{w:DynRes " + v[len("{DynamicResource "):]
    return v


PROP_RENAMES = {
    "IsMouseOver": "IsPointerOver",
    "IsKeyboardFocused": "IsFocused",
    "ActualWidth": "Bounds.Width",
    "ActualHeight": "Bounds.Height",
}


def map_prop_name(name):
    return PROP_RENAMES.get(name, name)


def convert_drop_shadow(el):
    depth = float(el.getAttribute("ShadowDepth") or 5) if el.hasAttribute("ShadowDepth") else 5.0
    direction = float(el.getAttribute("Direction") or 315) if el.hasAttribute("Direction") else 315.0
    for a in ("ShadowDepth", "Direction", "RenderingType"):
        if el.hasAttribute(a):
            el.removeAttribute(a)
    rad = math.radians(direction)
    ox = depth * math.cos(rad)
    oy = -depth * math.sin(rad)
    el.setAttribute("OffsetX", fmt_num(round(ox, 3)))
    el.setAttribute("OffsetY", fmt_num(round(oy, 3)))
    if not el.hasAttribute("BlurRadius"):
        el.setAttribute("BlurRadius", "5")


def setter_property(prop, style_target):
    """Map a WPF Setter Property name for Avalonia (non-trigger) setters."""
    p = prop.strip()
    owner, _, name = p.rpartition(".")
    ATTACHED = {("Canvas", "Left"), ("Canvas", "Top"), ("Canvas", "Right"), ("Canvas", "Bottom"), ("Grid", "Row"), ("Grid", "Column"),
                ("Grid", "RowSpan"), ("Grid", "ColumnSpan"), ("Grid", "IsSharedSizeScope"), ("DockPanel", "Dock"),
                ("ScrollViewer", "HorizontalScrollBarVisibility"), ("ScrollViewer", "VerticalScrollBarVisibility")}
    if (owner, name) in ATTACHED:
        return p
    if p.startswith("RenderOptions.") or p.startswith("TextOptions."):
        return None
    if owner in TRANSPARENT_OWNERS:
        if owner in ("TextBlock",) and name in ("TextAlignment", "Foreground", "FontSize", "FontWeight", "FontFamily") and style_target != "TextBlock":
            return {"TextAlignment": "TextBlock.TextAlignment"}.get(name, "TextElement." + name)
        p = name
    if p in ("Panel.ZIndex",):
        return "ZIndex"
    if p == "Visibility":
        return "w:Wpf.Visibility"
    if p == "ToolTip":
        return "ToolTip.Tip"
    if p in ("IsTabStop",):
        return "KeyboardNavigation.IsTabStop"
    if p in ("TextElement.Foreground", "TextElement.FontSize", "TextElement.FontWeight", "TextElement.FontFamily"):
        return p
    if p == "RenderOptions.BitmapScalingMode":
        return "RenderOptions.BitmapInterpolationMode"
    return p


def is_dropped_prop(prop):
    bare = prop.rpartition(".")[2]
    return prop in DROP_ATTRS or bare in {"FocusVisualStyle", "SnapsToDevicePixels", "OverridesDefaultStyle", "TextFormattingMode",
                                          "TextRenderingMode", "TextHintingMode", "IsFocusScope", "CanContentScroll", "PanningMode",
                                          "DirectionalNavigation", "ControlTabNavigation", "IsDeferredScrollingEnabled", "BitmapEffect",
                                          "IsVirtualizing", "VirtualizationMode", "ScrollUnit", "CacheMode", "IsMoveToPointEnabled",
                                          "LineStackingStrategy", "ForceCursor"}


class Converter:
    def __init__(self, doc, filename):
        self.doc = doc
        self.filename = filename
        self.ctx = Ctx()
        self.keyed_styles = {}  # key -> trigger depth used
        self.tunnel_events = []  # (element_name, avalonia_event, handler)
        self.named_objects = []  # (name, type, accessor expression)

    # ------------------------------------------------------------------ entry
    def run(self):
        root = self.doc.documentElement
        self.fix_namespaces(root)
        self.collect_named_objects(root)
        self.convert_element(root, None)
        return self.doc

    NON_CONTROL_TYPES = {"ScaleTransform", "TranslateTransform", "RotateTransform", "SkewTransform", "TransformGroup", "MatrixTransform",
                         "SolidColorBrush", "LinearGradientBrush", "RadialGradientBrush", "ImageBrush", "GradientStop", "DropShadowEffect",
                         "BlurEffect", "RectangleGeometry", "EllipseGeometry", "StreamGeometry", "PathGeometry", "GeometryGroup"}

    def collect_named_objects(self, root):
        """x:Name on transforms/brushes is not generated by Avalonia: replace with a generated accessor property."""
        def walk(el, in_template):
            ln = local(el.tagName)
            if ln in ("ControlTemplate", "DataTemplate", "ItemsPanelTemplate", "Style", "ResourceDictionary") and el is not root:
                in_template = True
            if ln in self.NON_CONTROL_TYPES and not in_template:
                name = el.getAttribute("x:Name") or el.getAttribute("Name")
                if name:
                    expr = self.accessor_for(el)
                    if expr:
                        self.named_objects.append((name, ln, expr))
                        for a in ("x:Name", "Name"):
                            if el.hasAttribute(a):
                                el.removeAttribute(a)
                    else:
                        warn("%s: cannot generate accessor for named %s %s" % (self.filename, ln, name))
            for c in children(el):
                walk(c, in_template)
        walk(root, False)

    def accessor_for(self, el):
        """Build a C# expression that reaches el from its owning named control."""
        steps = []
        node = el
        while True:
            parent = node.parentNode
            if parent is None or parent.nodeType != Node.ELEMENT_NODE:
                return None
            pl = local(parent.tagName)
            if "." in pl:  # property element: Owner.Prop
                prop = pl.rpartition(".")[2]
                owner = parent.parentNode
                if prop == "Children" and local(owner.tagName) == "TransformGroup":
                    idx = [c for c in children(parent)].index(node)
                    steps.append(("index", idx, local(node.tagName)))
                    node = owner
                    continue
                steps.append(("prop", prop, local(node.tagName)))
                if local(owner.tagName) == "TransformGroup":
                    node = owner
                    continue
                if local(owner.tagName) in self.NON_CONTROL_TYPES:
                    node = owner
                    continue
                if prop == "LayoutTransform":
                    steps[-1] = ("layout", prop, local(node.tagName))
                name = self.ensure_name(owner)
                expr = name
                for kind, value, typ in reversed(steps):
                    if kind == "prop":
                        expr = "((%s)%s.%s!)" % (typ, expr, value)
                    elif kind == "layout":
                        expr = "((%s)((Avalonia.Controls.LayoutTransformControl)%s.Parent!).LayoutTransform!)" % (typ, expr)
                    else:
                        expr = "((%s)((TransformGroup)%s).Children[%d])" % (typ, expr, value)
                return expr
            if pl == "TransformGroup":
                # implicit Children content
                idx = [c for c in children(parent)].index(node)
                steps.append(("index", idx, local(node.tagName)))
                node = parent
                continue
            return None

    def fix_namespaces(self, root):
        for attr in list(root.attributes.keys()):
            val = root.getAttribute(attr)
            if attr == "xmlns":
                root.setAttribute("xmlns", AVALONIA_NS)
            elif attr.startswith("xmlns:"):
                if "clr-namespace:" in val:
                    ns = val.split("clr-namespace:", 1)[1].split(";", 1)[0]
                    if ns.startswith("System.Windows.Forms"):
                        root.removeAttribute(attr)
                        continue
                    root.setAttribute(attr, "using:" + ns)
        root.setAttribute("xmlns:w", COMPAT_NS)

    # ------------------------------------------------------------------ helpers
    def rename(self, el, new_tag):
        el.tagName = new_tag
        el.nodeName = new_tag

    def new_el(self, tag):
        return self.doc.createElement(tag)

    def owner_tag_for(self, el):
        parent = el.parentNode
        return parent.tagName if parent is not None and parent.nodeType == Node.ELEMENT_NODE else None

    def ensure_name(self, el):
        for a in ("x:Name", "Name"):
            if el.hasAttribute(a):
                return el.getAttribute(a)
        self.ctx.auto_name += 1
        n = "__WpfEvt%d" % self.ctx.auto_name
        el.setAttribute("x:Name", n)
        return n

    # ------------------------------------------------------------------ main recursion
    def convert_element(self, el, parent):
        tag = el.tagName
        name = local(tag)
        pfx = prefix(tag)

        if pfx == "wfi" or name == "WindowsFormsHost":
            self.rename(el, "w:VideoView")
            for a in list(el.attributes.keys()):
                if a not in ("Name", "x:Name"):
                    el.removeAttribute(a)
            return

        # property elements
        if "." in name and pfx in ("", "controls", "views", "w") and not name.startswith("Storyboard"):
            self.convert_property_element(el, parent)
            return

        if pfx == "":
            if name == "Style":
                self.convert_style(el, parent)
                return
            if name == "ControlTemplate":
                self.convert_control_template(el)
                return
            if name == "DataTemplate":
                self.convert_data_template(el)
                return
            if name == "DropShadowEffect":
                convert_drop_shadow(el)
            if name in ("LinearGradientBrush",):
                if el.getAttribute("MappingMode") != "Absolute":
                    for a in ("StartPoint", "EndPoint"):
                        if el.hasAttribute(a):
                            el.setAttribute(a, rel_point(el.getAttribute(a)))
                    if not el.hasAttribute("StartPoint"):
                        el.setAttribute("StartPoint", "0%,0%")
                    if not el.hasAttribute("EndPoint"):
                        el.setAttribute("EndPoint", "100%,100%")
                if el.hasAttribute("MappingMode"):
                    el.removeAttribute("MappingMode")
            if name == "RadialGradientBrush":
                for a in ("Center", "GradientOrigin"):
                    if el.hasAttribute(a):
                        el.setAttribute(a, rel_point(el.getAttribute(a)))
                for a in ("RadiusX", "RadiusY"):
                    if el.hasAttribute(a):
                        el.setAttribute(a, rel_scalar(el.getAttribute(a)))
                if el.hasAttribute("MappingMode"):
                    el.removeAttribute("MappingMode")
            if name == "ImageBrush":
                if el.hasAttribute("ImageSource"):
                    src = el.getAttribute("ImageSource")
                    el.removeAttribute("ImageSource")
                    el.setAttribute("Source", src)
                for a in ("Viewbox", "ViewboxUnits", "Viewport", "ViewportUnits"):
                    if el.hasAttribute(a):
                        warn("%s: ImageBrush.%s dropped" % (self.filename, a))
                        el.removeAttribute(a)
            if name == "Geometry":
                self.rename(el, "StreamGeometry")
            if name == "Line":
                x1, y1, x2, y2 = (el.getAttribute(a) or "0" for a in ("X1", "Y1", "X2", "Y2"))
                for a in ("X1", "Y1", "X2", "Y2"):
                    if el.hasAttribute(a):
                        el.removeAttribute(a)
                el.setAttribute("StartPoint", "%s,%s" % (x1, y1))
                el.setAttribute("EndPoint", "%s,%s" % (x2, y2))
            if name == "ScaleTransform" and (el.hasAttribute("CenterX") or el.hasAttribute("CenterY")):
                warn("%s: ScaleTransform CenterX/Y dropped" % self.filename)
                for a in ("CenterX", "CenterY"):
                    if el.hasAttribute(a):
                        el.removeAttribute(a)
            if name == "ContentPresenter" and self.ctx.in_template:
                if not el.hasAttribute("Content"):
                    el.setAttribute("Content", "{TemplateBinding Content}")
                if not el.hasAttribute("ContentTemplate"):
                    el.setAttribute("ContentTemplate", "{TemplateBinding ContentTemplate}")
                if not el.hasAttribute("Foreground"):
                    el.setAttribute("Foreground", "{TemplateBinding Foreground}")
                if not el.hasAttribute("FontSize"):
                    el.setAttribute("FontSize", "{TemplateBinding FontSize}")
                if not el.hasAttribute("FontWeight"):
                    el.setAttribute("FontWeight", "{TemplateBinding FontWeight}")
                if not el.hasAttribute("FontFamily"):
                    el.setAttribute("FontFamily", "{TemplateBinding FontFamily}")
                if el.hasAttribute("ContentSource"):
                    el.removeAttribute("ContentSource")
            if name == "Run":
                pass
            if name == "Window":
                self.convert_window_attrs(el)
            if name == "Application":
                self.convert_application(el)
                return
            if name == "Popup":
                if el.hasAttribute("PopupAnimation"):
                    el.removeAttribute("PopupAnimation")
            if name in ("BooleanToVisibilityConverter",):
                self.rename(el, "w:BooleanToVisibilityConverter")
            if name == "Canvas":
                for a in ("Left", "Top", "Right", "Bottom"):
                    if el.hasAttribute(a):
                        el.setAttribute("Canvas." + a, el.getAttribute(a))
                        el.removeAttribute(a)
            if name == "ResourceDictionary" and el.hasAttribute("Source") and parent is not None and local(parent.tagName) == "ResourceDictionary.MergedDictionaries":
                src = el.getAttribute("Source").lstrip("/")
                src = src.replace("XboxMetroLauncher.Themes/", "bladethemes/")
                src = re.sub(r"\.xaml$", ".axaml", src, flags=re.I)
                self.rename(el, "ResourceInclude")
                el.removeAttribute("Source")
                el.setAttribute("Source", "avares://DashX360/Xaml/" + src.lower() if False else "avares://DashX360/Xaml/" + src)
                return

        if pfx == "" and name in WPF_STRETCH_DEFAULTS and not el.hasAttribute("Style") and not any(local(c.tagName) in (name + ".Style",) for c in children(el)):
            for prop in ("HorizontalAlignment", "VerticalAlignment"):
                if not el.hasAttribute(prop):
                    el.setAttribute(prop, "Stretch")
        self.convert_attributes(el)
        self.handle_layout_transform(el)
        self.handle_render_transform_origin(el)
        for c in children(el):
            self.convert_element(c, el)

    # ------------------------------------------------------------------ attributes
    def convert_attributes(self, el):
        tag_local = local(el.tagName)
        for attr in list(el.attributes.keys()):
            if attr.startswith("xmlns") or attr in ("x:Class", "x:Key", "x:Name", "x:Shared"):
                continue
            val = el.getAttribute(attr)
            if any(0xE000 <= ord(ch) <= 0xF8FF for ch in val):
                val = map_icon_glyphs(val)
                el.setAttribute(attr, val)
            if attr in DROP_ATTRS or is_dropped_prop(attr):
                el.removeAttribute(attr)
                continue
            if attr == "Visibility" or attr.endswith(".Visibility") and attr.split(".")[0] in TRANSPARENT_OWNERS:
                el.removeAttribute(attr)
                el.setAttribute("w:Wpf.Visibility", convert_markup(val, self.ctx))
                continue
            if attr in ("Panel.ZIndex",):
                el.removeAttribute(attr)
                el.setAttribute("ZIndex", val)
                continue
            if attr == "ToolTip":
                el.removeAttribute(attr)
                el.setAttribute("ToolTip.Tip", val)
                continue
            if attr == "IsTabStop":
                el.removeAttribute(attr)
                el.setAttribute("KeyboardNavigation.IsTabStop", val)
                continue
            if attr in ("Style", "FrameworkElement.Style"):
                el.removeAttribute(attr)
                if val.strip() in ("{x:Null}",):
                    continue
                el.setAttribute("Theme", val)
                continue
            if attr == "ItemContainerStyle":
                el.removeAttribute(attr)
                el.setAttribute("ItemContainerTheme", val)
                continue
            if attr == "RenderOptions.BitmapScalingMode":
                el.removeAttribute(attr)
                el.setAttribute("RenderOptions.BitmapInterpolationMode", {"HighQuality": "HighQuality", "Fant": "HighQuality", "NearestNeighbor": "None", "LowQuality": "LowQuality", "Linear": "LowQuality"}.get(val, "HighQuality"))
                continue
            if attr in ("FontFamily", "TextElement.FontFamily", "TextBlock.FontFamily"):
                el.setAttribute(attr, fix_font_family(val))
                continue
            if attr in ("Width", "Height", "MinWidth", "MinHeight", "MaxWidth", "MaxHeight") and val.strip().lower() == "auto" and tag_local not in ("ColumnDefinition", "RowDefinition"):
                el.setAttribute(attr, "NaN")
                continue
            if attr == "RenderTransformOrigin":
                el.setAttribute(attr, rel_point(val))
                continue
            if attr in ("TextBlock.TextAlignment",) and tag_local != "TextBlock":
                continue
            if attr == "IsEditable" and tag_local == "ComboBox":
                continue
            if attr == "Source" and tag_local in ("Image", "ImageBrush") and not val.startswith("{"):
                path = re.sub(r"^pack://siteoforigin:,,,/", "", val).lstrip("/")
                el.setAttribute(attr, "{Binding Source=%s, Converter={StaticResource StringToImageSourceConverter}}" % path)
                continue
            if attr == "StrokeStartLineCap":
                el.removeAttribute(attr)
                el.setAttribute("StrokeLineCap", val)
                continue
            if attr == "StrokeEndLineCap":
                el.removeAttribute(attr)
                continue
            if attr in ("VerticalScrollBarVisibility", "HorizontalScrollBarVisibility") and tag_local in ("TextBox", "ListBox", "ComboBox", "ItemsControl"):
                el.removeAttribute(attr)
                el.setAttribute("ScrollViewer." + attr, val)
                continue
            if attr == "DisplayMemberPath":
                el.removeAttribute(attr)
                el.setAttribute("DisplayMemberBinding", "{Binding %s}" % val)
                continue
            if attr in ("RenderOptions.ClearTypeHint", "RenderOptions.CachingHint"):
                el.removeAttribute(attr)
                continue
            if attr in TUNNEL_EVENTS:
                el.removeAttribute(attr)
                ev, _args = TUNNEL_EVENTS[attr]
                target = "this" if el.parentNode is self.doc else self.ensure_name(el)
                self.tunnel_events.append((target, ev, val))
                continue
            if attr in EVENT_RENAMES and EVENT_RENAMES[attr] != attr:
                el.removeAttribute(attr)
                el.setAttribute(EVENT_RENAMES[attr], val)
                continue
            if attr == "IsVisibleChanged":
                el.removeAttribute(attr)
                warn("%s: IsVisibleChanged handler %s must be wired in code" % (self.filename, val))
                continue
            owner, dot, bare = attr.rpartition(".")
            if dot and owner in ("UIElement", "FrameworkElement", "Control", "Panel", "Shape", "ContentControl", "ItemsControl", "TextBoxBase"):
                el.removeAttribute(attr)
                if bare == "ZIndex":
                    el.setAttribute("ZIndex", val)
                elif bare == "Visibility":
                    el.setAttribute("w:Wpf.Visibility", convert_markup(val, self.ctx))
                else:
                    el.setAttribute(map_prop_name(bare), convert_markup(val, self.ctx))
                continue
            if val.startswith("{"):
                el.setAttribute(attr, convert_markup(val, self.ctx, attr))

    def convert_window_attrs(self, el):
        if el.getAttribute("WindowStyle") == "None":
            el.setAttribute("SystemDecorations", "None")
        if el.getAttribute("AllowsTransparency") == "True":
            el.setAttribute("TransparencyLevelHint", "Transparent")
        # WPF NoResize still lets a window maximize/fullscreen; on X11 Avalonia's CanResize=False pins min=max size,
        # which stops the window manager from growing it. Borderless windows can't be resized by the user anyway.
        if el.getAttribute("ResizeMode") == "NoResize" and el.getAttribute("WindowStyle") != "None":
            el.setAttribute("CanResize", "False")
        for a in ("WindowStyle", "AllowsTransparency", "ResizeMode"):
            if el.hasAttribute(a):
                el.removeAttribute(a)

    # ------------------------------------------------------------------ layout helpers
    def handle_render_transform_origin(self, el):
        has_rt = el.hasAttribute("RenderTransform") or any(local(c.tagName).endswith(".RenderTransform") for c in children(el))
        if has_rt and not el.hasAttribute("RenderTransformOrigin"):
            el.setAttribute("RenderTransformOrigin", "0%,0%")

    def handle_layout_transform(self, el):
        lt = [c for c in children(el) if local(c.tagName).endswith(".LayoutTransform")]
        if not lt:
            return
        lt = lt[0]
        parent = el.parentNode
        wrapper = self.new_el("LayoutTransformControl")
        moved = ("Grid.Row", "Grid.Column", "Grid.RowSpan", "Grid.ColumnSpan", "Canvas.Left", "Canvas.Top", "Canvas.Right", "Canvas.Bottom",
                 "DockPanel.Dock", "HorizontalAlignment", "VerticalAlignment", "Margin", "ZIndex", "w:Wpf.Visibility")
        for a in moved:
            if el.hasAttribute(a):
                wrapper.setAttribute(a, el.getAttribute(a))
                el.removeAttribute(a)
        el.removeChild(lt)
        self.rename(lt, "LayoutTransformControl.LayoutTransform")
        parent.replaceChild(wrapper, el)
        wrapper.appendChild(lt)
        wrapper.appendChild(el)

    # ------------------------------------------------------------------ property elements
    def convert_property_element(self, el, parent):
        name = local(el.tagName)
        owner, _, prop = name.rpartition(".")
        parent_tag = parent.tagName if parent is not None else owner
        parent_local = local(parent_tag)

        if owner in ("ControlTemplate",) and prop == "Triggers":
            return  # handled by convert_control_template
        if owner in ("TriggerBase", "Trigger", "DataTrigger", "MultiTrigger", "MultiDataTrigger", "EventTrigger"):
            self.rename(el, "%s.%s" % (parent_tag, prop))
            for c in children(el):
                self.convert_element(c, el)
            return
        if prop in ("Style",) and owner in TRANSPARENT_OWNERS | {parent_local}:
            # inline style -> Theme (ControlTheme)
            self.rename(el, "%s.Theme" % parent_tag)
            for c in children(el):
                if local(c.tagName) == "Style":
                    self.convert_style(c, el, inline_target=parent_local)
            return
        if prop == "ItemContainerStyle":
            self.rename(el, "%s.ItemContainerTheme" % parent_tag)
            for c in children(el):
                if local(c.tagName) == "Style":
                    self.convert_style(c, el)
            return
        if prop == "Resources" and owner in TRANSPARENT_OWNERS | {parent_local, "Application", "Window"}:
            self.rename(el, "%s.Resources" % parent_tag)
            self.convert_resources(el, parent)
            return
        if prop == "LayoutTransform":
            for c in children(el):
                self.convert_element(c, el)
            return
        if prop in ("Effect",):
            self.rename(el, "%s.Effect" % parent_tag)
            for c in children(el):
                self.convert_element(c, el)
            return
        if prop == "Visibility":
            self.rename(el, "w:Wpf.Visibility")
            return
        if prop == "ToolTip":
            self.rename(el, "ToolTip.Tip")
            for c in children(el):
                self.convert_element(c, el)
            return
        if owner in TRANSPARENT_OWNERS:
            self.rename(el, "%s.%s" % (parent_tag, map_prop_name(prop)))
        for c in children(el):
            self.convert_element(c, el)

    # ------------------------------------------------------------------ resources / styles
    def convert_resources(self, el, owner_el):
        """Convert a .Resources element; implicit (type keyed) styles move to .Styles."""
        container = el
        rds = [c for c in children(el) if local(c.tagName) == "ResourceDictionary"]
        if len(rds) == 1 and not rds[0].hasAttribute("Source"):
            container = rds[0]
        implicit = []
        for c in children(container):
            if local(c.tagName) == "Style":
                key = c.getAttribute("x:Key")
                if not key or key.startswith("{x:Type"):
                    implicit.append(c)
        for c in implicit:
            container.removeChild(c)
        for c in children(container):
            if local(c.tagName) == "ResourceDictionary.MergedDictionaries":
                for rd in children(c):
                    self.convert_element(rd, c)
            elif local(c.tagName) == "Style":
                self.convert_style(c, container)
            else:
                self.convert_element(c, container)
        if implicit and owner_el is not None:
            styles = self.new_el("%s.Styles" % owner_el.tagName)
            owner_el.insertBefore(styles, el.nextSibling)
            for s in implicit:
                styles.appendChild(s)
                self.convert_selector_style(s, styles)

    def style_target(self, style_el, inline_target=None):
        tt = style_el.getAttribute("TargetType")
        m = re.match(r"\{x:Type\s+([\w:]+)\}", tt)
        if m:
            tt = m.group(1)
        if not tt:
            tt = inline_target or ""
        return tt

    def collect_setters_and_triggers(self, style_el, target):
        setters, triggers, resources, others = [], [], [], []
        for c in children(style_el):
            ln = local(c.tagName)
            if ln == "Setter":
                setters.append(c)
            elif ln == "Style.Triggers":
                triggers.extend(children(c))
            elif ln == "Style.Resources":
                resources.append(c)
            elif ln == "EventSetter":
                warn("%s: EventSetter %s=%s must be wired in code (style %s)" % (self.filename, c.getAttribute("Event"), c.getAttribute("Handler"), style_el.getAttribute("x:Key") or target))
                style_el.removeChild(c)
            else:
                others.append(c)
        return setters, triggers, resources, others

    def convert_setter(self, setter, target):
        prop = setter.getAttribute("Property")
        if is_dropped_prop(prop):
            setter.parentNode.removeChild(setter)
            return False
        mapped = setter_property(prop, target)
        if mapped is None:
            setter.parentNode.removeChild(setter)
            return False
        setter.setAttribute("Property", mapped)
        if setter.hasAttribute("TargetName"):
            warn("%s: style setter with TargetName dropped" % self.filename)
            setter.removeAttribute("TargetName")
        if setter.hasAttribute("Value"):
            v = map_icon_glyphs(setter.getAttribute("Value"))
            if setter.getAttribute("Property") == "RenderTransformOrigin":
                v = rel_point(v)
            if setter.getAttribute("Property") in ("FontFamily", "TextElement.FontFamily"):
                v = fix_font_family(v)
            if setter.getAttribute("Property") in ("Width", "Height") and v.strip().lower() == "auto":
                v = "NaN"
            if setter.getAttribute("Property") == "RenderOptions.BitmapInterpolationMode":
                v = {"Fant": "HighQuality", "NearestNeighbor": "None", "Linear": "LowQuality"}.get(v, v)
            setter.setAttribute("Value", convert_markup(v, self.ctx))
        for c in children(setter):
            if local(c.tagName) == "Setter.Value":
                for cc in children(c):
                    self.convert_element(cc, c)
        return True

    def build_trigger_setter(self, triggers, prop, in_template=False):
        """Create <Setter Property="prop"><Template><w:TriggerCollection>...</w:TriggerCollection></Template></Setter>."""
        setter = self.new_el("Setter")
        setter.setAttribute("Property", prop)
        tmpl = self.new_el("Template")
        coll = self.new_el("w:TriggerCollection")
        setter.appendChild(tmpl)
        tmpl.appendChild(coll)
        for t in triggers:
            t.parentNode.removeChild(t)
            coll.appendChild(t)
            self.convert_trigger(t, in_template=in_template)
        return setter

    def base_depth(self, style_el):
        based = style_el.getAttribute("BasedOn")
        m = re.match(r"\{StaticResource\s+([\w.]+)\}", based or "")
        if m and m.group(1) in self.keyed_styles:
            return self.keyed_styles[m.group(1)] + 1
        return 0

    def convert_style(self, style_el, parent, inline_target=None):
        """WPF keyed/inline Style -> Avalonia ControlTheme."""
        target = self.style_target(style_el, inline_target)
        key = style_el.getAttribute("x:Key")
        if key.startswith("{x:Type") or (not key and inline_target is None and local(parent.tagName) in ("ResourceDictionary",)):
            warn("%s: implicit style for %s in resource dictionary dropped (fonts are applied globally)" % (self.filename, target))
            parent.removeChild(style_el)
            return
        if not target:
            warn("%s: Style without TargetType (%s) dropped" % (self.filename, key))
            parent.removeChild(style_el)
            return
        self.rename(style_el, "ControlTheme")
        style_el.setAttribute("TargetType", target)
        depth = self.base_depth(style_el)
        based = style_el.getAttribute("BasedOn")
        if based:
            m = re.match(r"\{StaticResource\s+\{x:Type\s+([\w:]+)\}\s*\}", based)
            if m:
                style_el.setAttribute("BasedOn", "{StaticResource {x:Type %s}}" % m.group(1))
        elif local(target) in TEMPLATED_CONTROLS and local(target) not in ("UserControl", "Window"):
            style_el.setAttribute("BasedOn", "{StaticResource {x:Type %s}}" % target)
        setters, triggers, resources, others = self.collect_setters_and_triggers(style_el, target)
        if not triggers:
            for c in children(style_el):
                if local(c.tagName) == "Style.Triggers":
                    style_el.removeChild(c)
        for s in setters:
            self.convert_setter(s, local(target))
        if local(target) in WPF_STRETCH_DEFAULTS:
            # WPF controls stretch by default; Fluent buttons align Left/Center.
            existing = {c.getAttribute("Property") for c in children(style_el) if local(c.tagName) == "Setter"}
            for prop in ("HorizontalAlignment", "VerticalAlignment"):
                if prop not in existing:
                    st = self.new_el("Setter")
                    st.setAttribute("Property", prop)
                    st.setAttribute("Value", "Stretch")
                    style_el.insertBefore(st, style_el.firstChild)
        if local(target) in ("ContentControl", "UserControl") and not style_el.getAttribute("BasedOn") and not any(c.getAttribute("Property") == "Template" for c in setters):
            # No Fluent theme to inherit from: provide the default content template.
            tmpl_setter = self.new_el("Setter")
            tmpl_setter.setAttribute("Property", "Template")
            frag = minidom.parseString('<r xmlns="%s">%s</r>' % (AVALONIA_NS, CONTENT_TEMPLATE)).documentElement.firstChild
            tmpl_setter.appendChild(self.doc.importNode(frag, True))
            for node in [tmpl_setter.firstChild]:
                if node.hasAttribute("xmlns"):
                    node.removeAttribute("xmlns")
            style_el.insertBefore(tmpl_setter, style_el.firstChild)
        for r in resources:
            self.rename(r, "ControlTheme.Resources")
            self.convert_resources(r, None)
        if triggers:
            prop = ["w:WpfTriggers.ThemeTriggers", "w:WpfTriggers.ThemeTriggers2", "w:WpfTriggers.ThemeTriggers3"][min(depth, 2)]
            trig_parent = [c for c in children(style_el) if local(c.tagName) == "Style.Triggers"][0]
            setter = self.build_trigger_setter(triggers, prop)
            style_el.replaceChild(setter, trig_parent)
        if key:
            self.keyed_styles[key] = depth

    def convert_selector_style(self, style_el, parent):
        target = self.style_target(style_el)
        if style_el.hasAttribute("x:Key"):
            style_el.removeAttribute("x:Key")
        style_el.removeAttribute("TargetType")
        if style_el.hasAttribute("BasedOn"):
            style_el.removeAttribute("BasedOn")
        style_el.setAttribute("Selector", target)
        setters, triggers, resources, others = self.collect_setters_and_triggers(style_el, target)
        if not triggers:
            for c in children(style_el):
                if local(c.tagName) == "Style.Triggers":
                    style_el.removeChild(c)
        for s in setters:
            self.convert_setter(s, local(target))
        for r in resources:
            self.rename(r, "Style.Resources")
            self.convert_resources(r, None)
        if triggers:
            trig_parent = [c for c in children(style_el) if local(c.tagName) == "Style.Triggers"][0]
            setter = self.build_trigger_setter(triggers, "w:WpfTriggers.StyleTriggers")
            style_el.replaceChild(setter, trig_parent)

    # ------------------------------------------------------------------ templates
    def convert_control_template(self, el):
        tt = el.getAttribute("TargetType")
        m = re.match(r"\{x:Type\s+([\w:]+)\}", tt)
        if m:
            el.setAttribute("TargetType", m.group(1))
        prev = self.ctx.in_template
        self.ctx.in_template = True
        triggers_el = [c for c in children(el) if local(c.tagName) == "ControlTemplate.Triggers"]
        roots = [c for c in children(el) if local(c.tagName) != "ControlTemplate.Triggers" and not local(c.tagName).startswith("ControlTemplate.")]
        for r in children(el):
            if local(r.tagName).startswith("ControlTemplate.Resources"):
                warn("%s: ControlTemplate.Resources moved to root" % self.filename)
        if len(roots) != 1:
            warn("%s: ControlTemplate with %d roots" % (self.filename, len(roots)))
        if triggers_el and roots:
            root = roots[0]
            trig = triggers_el[0]
            el.removeChild(trig)
            prop_el = self.new_el("%s.%s" % ("w:WpfTriggers", "TemplateTriggers"))
            coll = self.new_el("w:TriggerCollection")
            prop_el.appendChild(coll)
            for t in children(trig):
                trig.removeChild(t)
                coll.appendChild(t)
            for r in roots:
                self.convert_element(r, el)
            root = [c for c in children(el)][0]
            root.insertBefore(prop_el, root.firstChild)
            for t in children(coll):
                self.convert_trigger(t, in_template=True)
        else:
            for r in roots:
                self.convert_element(r, el)
        self.ctx.in_template = prev

    def convert_data_template(self, el):
        dt = el.getAttribute("DataType")
        m = re.match(r"\{x:Type\s+([\w:]+)\}", dt)
        if m:
            el.setAttribute("DataType", m.group(1))
        if el.hasAttribute("x:Key") and el.getAttribute("x:Key").startswith("{DataTemplateKey"):
            el.removeAttribute("x:Key")
        prev = self.ctx.in_template
        self.ctx.in_template = False
        triggers_el = [c for c in children(el) if local(c.tagName) == "DataTemplate.Triggers"]
        roots = [c for c in children(el) if not local(c.tagName).startswith("DataTemplate.")]
        for r in roots:
            self.convert_element(r, el)
        if triggers_el and roots:
            trig = triggers_el[0]
            el.removeChild(trig)
            root = [c for c in children(el) if not local(c.tagName).startswith("DataTemplate.")][0]
            prop_el = self.new_el("w:WpfTriggers.DataTemplateTriggers")
            coll = self.new_el("w:TriggerCollection")
            prop_el.appendChild(coll)
            for t in children(trig):
                trig.removeChild(t)
                coll.appendChild(t)
            root.insertBefore(prop_el, root.firstChild)
            for t in children(coll):
                self.convert_trigger(t, in_template=False)
        self.ctx.in_template = prev

    # ------------------------------------------------------------------ triggers
    def convert_trigger(self, t, in_template):
        prev_t, prev_tr = self.ctx.in_template, self.ctx.in_trigger
        self.ctx.in_template = in_template
        self.ctx.in_trigger = True
        ln = local(t.tagName)
        if ln not in ("Trigger", "DataTrigger", "MultiTrigger", "MultiDataTrigger"):
            warn("%s: unsupported trigger %s dropped" % (self.filename, ln))
            t.parentNode.removeChild(t)
            self.ctx.in_template, self.ctx.in_trigger = prev_t, prev_tr
            return
        self.rename(t, "w:" + ln)
        for a in ("Binding", "Value", "Property"):
            if t.hasAttribute(a):
                t.setAttribute(a, convert_markup(t.getAttribute(a), self.ctx))
        for c in children(t):
            cl = local(c.tagName)
            if cl == "Setter":
                self.convert_trigger_setter(c)
            elif cl.endswith(".Conditions"):
                self.rename(c, "w:%s.Conditions" % ln)
                for cond in children(c):
                    self.rename(cond, "w:Condition")
                    for a in ("Binding", "Value"):
                        if cond.hasAttribute(a):
                            cond.setAttribute(a, convert_markup(cond.getAttribute(a), self.ctx))
            elif cl.endswith(".EnterActions") or cl.endswith(".ExitActions"):
                which = cl.rpartition(".")[2]
                self.rename(c, "w:%s.%s" % (ln, which))
                for action in children(c):
                    self.convert_trigger_action(action)
            elif cl.endswith(".Setters"):
                for s in children(c):
                    c.removeChild(s)
                    t.insertBefore(s, c)
                    self.convert_trigger_setter(s)
                t.removeChild(c)
            else:
                warn("%s: unknown trigger child %s" % (self.filename, cl))
        self.ctx.in_template, self.ctx.in_trigger = prev_t, prev_tr

    def convert_trigger_setter(self, s):
        self.rename(s, "w:Setter")
        if s.hasAttribute("Value"):
            v = s.getAttribute("Value")
            prop = s.getAttribute("Property")
            if prop.rpartition(".")[2] == "RenderTransformOrigin":
                v = rel_point(v)
            if prop.rpartition(".")[2] == "FontFamily":
                v = fix_font_family(v)
            s.setAttribute("Value", convert_markup(v, self.ctx))
        for c in children(s):
            if local(c.tagName) == "Setter.Value":
                self.rename(c, "w:Setter.Value")
                prev = self.ctx.in_trigger
                self.ctx.in_trigger = False
                for cc in children(c):
                    self.convert_element(cc, c)
                self.ctx.in_trigger = prev

    def convert_trigger_action(self, a):
        ln = local(a.tagName)
        self.rename(a, "w:" + ln)
        for c in children(a):
            if local(c.tagName) == "Storyboard":
                self.convert_storyboard(c)

    def convert_storyboard(self, sb):
        self.rename(sb, "w:Storyboard")
        self.convert_timeline_attrs(sb)
        for c in children(sb):
            self.convert_timeline(c)

    def convert_timeline_attrs(self, el):
        for a in list(el.attributes.keys()):
            if a == "Storyboard.TargetName":
                el.setAttribute("TargetName", el.getAttribute(a))
                el.removeAttribute(a)
            elif a == "Storyboard.TargetProperty":
                el.setAttribute("TargetProperty", el.getAttribute(a))
                el.removeAttribute(a)

    def convert_timeline(self, el):
        ln = local(el.tagName)
        if "." in ln:
            owner, _, prop = ln.rpartition(".")
            self.rename(el, "w:%s.%s" % (owner, prop))
            for c in children(el):
                self.convert_timeline(c)
            return
        self.rename(el, "w:" + ln)
        self.convert_timeline_attrs(el)
        for a in ("To", "From", "By"):
            if el.hasAttribute(a):
                el.setAttribute(a, convert_markup(el.getAttribute(a), self.ctx))
        for c in children(el):
            self.convert_timeline(c)

    # ------------------------------------------------------------------ application
    def convert_application(self, el):
        res = [c for c in children(el) if local(c.tagName) == "Application.Resources"]
        el.setAttribute("RequestedThemeVariant", "Light")
        styles = self.new_el("Application.Styles")
        extra = minidom.parseString('''<r xmlns="%s" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <FluentTheme>
    <FluentTheme.Palettes>
      <ColorPaletteResources x:Key="Light" Accent="#FF028D02" />
      <ColorPaletteResources x:Key="Dark" Accent="#FF028D02" />
    </FluentTheme.Palettes>
  </FluentTheme>
  <Style Selector="CheckBox /template/ ContentPresenter#PART_ContentPresenter">
    <Setter Property="Foreground" Value="{Binding $parent[CheckBox].Foreground}" />
  </Style>
  <Style Selector="RadioButton /template/ ContentPresenter#PART_ContentPresenter">
    <Setter Property="Foreground" Value="{Binding $parent[RadioButton].Foreground}" />
  </Style>
</r>''' % AVALONIA_NS).documentElement
        for node in list(extra.childNodes):
            if node.nodeType == Node.ELEMENT_NODE:
                imported = self.doc.importNode(node, True)
                styles.appendChild(imported)
        fluent = None
        cp = self.new_el("StyleInclude")
        cp.setAttribute("Source", "avares://Avalonia.Controls.ColorPicker/Themes/Fluent/Fluent.xaml")
        styles.appendChild(cp)
        el.insertBefore(styles, el.firstChild)
        if not res:
            return
        res = res[0]
        rd = [c for c in children(res) if local(c.tagName) == "ResourceDictionary"]
        container = rd[0] if rd else res
        templates = [c for c in children(container) if local(c.tagName) == "DataTemplate"]
        if templates:
            dts = self.new_el("Application.DataTemplates")
            el.insertBefore(dts, res)
            for t in templates:
                container.removeChild(t)
                dts.appendChild(t)
                self.convert_data_template(t)
        self.convert_resources(res, el)


def write_events_file(conv, out_cs, cls):
    if not conv.tunnel_events and not conv.named_objects:
        return
    ns, _, name = cls.rpartition(".")
    lines = [
        "// <auto-generated> by tools/wpf2avalonia.py: WPF Preview* (tunneling) event wiring.</auto-generated>",
        "using Avalonia.Input;",
        "using Avalonia.Media;",
        "using Avalonia.Interactivity;",
        "",
        "namespace %s;" % ns,
        "",
        "public partial class %s" % name,
        "{",
        "\tprivate void WireWpfTunnelEvents()",
        "\t{",
    ]
    for target, ev, handler in conv.tunnel_events:
        owner = "TextInputEvent" if ev == "TextInputEvent" else ev
        lines.append("\t\t%s.AddHandler(InputElement.%s, %s, RoutingStrategies.Tunnel);" % (target, owner, handler))
    lines.append("\t}")
    for name, typ, expr in conv.named_objects:
        lines.append("")
        lines.append("\tprivate Avalonia.Media.%s %s => %s;" % (typ, name, expr.replace("((TransformGroup)", "((Avalonia.Media.TransformGroup)")))
    lines += ["}", ""]
    with open(out_cs, "w", encoding="utf-8") as f:
        f.write("\n".join(lines))


def main():
    src, dst = sys.argv[1], sys.argv[2]
    events_cs = None
    if "--events" in sys.argv:
        events_cs = sys.argv[sys.argv.index("--events") + 1]
    with open(src, "rb") as f:
        data = f.read()
    if data.startswith(b"\xef\xbb\xbf"):
        data = data[3:]
    doc = minidom.parseString(data)
    conv = Converter(doc, src)
    conv.run()
    root = doc.documentElement
    xml = root.toxml()
    with open(dst, "w", encoding="utf-8") as f:
        f.write(xml + "\n")
    cls = root.getAttribute("x:Class")
    if events_cs and cls:
        write_events_file(conv, events_cs, cls)
    for r in report:
        print("  ! " + r)


if __name__ == "__main__":
    main()
