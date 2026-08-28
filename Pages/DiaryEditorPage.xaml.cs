/* ========== DiaryEditorPage - Diary Editor ==========
Function: WebView2 rich-text editor - HTML template injection, B/I/U format states, floating toolbar capsule, XSS sanitize, save/exit guards
Corresponding UI: DiaryEditorPage.xaml.cs
Logic Range: Whole file business logic of this module
*/
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Input;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT.Interop;
using Novara.Models;
using Novara.Services;
using Windows.Storage;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using AngleSharp.Html.Parser;
using DomNode = AngleSharp.Dom.INode;
using DomElement = AngleSharp.Dom.IElement;
using DomComment = AngleSharp.Dom.IComment;

namespace Novara.Pages;

public sealed partial class DiaryEditorPage : Page
{
    private bool _isToolbarCollapsed;
    private bool _isColorPickerOpen;
    private bool _isAlignPickerOpen;
    private DiaryEntry? _currentDiary;
    private string? _navigatingFormat; // N4D-02: format of the template navigation currently in flight - lets LoadDiary detect a mid-flight format switch
    private bool _isBold, _isItalic, _isUnderline;
    private bool _webViewReady;
    private string _loadedFormat = "html"; 
    private string? _initialTitle;   
    private string? _initialContent; 
    private bool _isMdHeadingPickerOpen; 
    private bool _isMdPreview; 
    private static readonly JsonSerializerOptions JsonCaseInsensitive = new() { PropertyNameCaseInsensitive = true };

    
    private const long MaxDiaryImageBytes = 20L * 1024 * 1024;
    private long _currentImageBytes; 

    /* ========== DiaryEditor HTML Template ==========
Function: Editor HTML/JS template: contenteditable body, setAll/getTitle/getBody bridge, execCommand helpers, format-state notify
Corresponding UI: DiaryEditorPage.xaml.cs
Logic Range: Below methods in this region
*/
private const string EditorHtmlTemplate = @"<!DOCTYPE html>
<html><head><meta charset='utf-8'><style>
*{{margin:0;padding:0;box-sizing:border-box}}
html,body{{background:{0};font-family:'Segoe UI',sans-serif;color:{1}}}
body{{overflow-y:auto;color:{1};padding-bottom:80px}}
body::-webkit-scrollbar{{width:6px}}body::-webkit-scrollbar-track{{background:transparent}}body::-webkit-scrollbar-thumb{{background:{5};border-radius:3px}}
::selection{{background:{4};color:inherit}}
#title{{font-size:17px;font-weight:normal;letter-spacing:0.18em;line-height:26px;outline:none;padding:4px 0 8px 0;word-wrap:break-word;color:{1}}}
#title b,#title strong,#title span[style*=bold]{{font-weight:700!important}}
#title:empty::before{{content:'{6}';color:{2};font-weight:normal;letter-spacing:0.18em}}
.sep{{height:1px;background:{3};margin:0 0 16px 0;opacity:0.45}}
#body{{min-height:calc(100vh - 150px)}}
#body .ProseMirror{{font-size:14px;font-weight:normal;letter-spacing:0.04em;line-height:20px;outline:none;min-height:calc(100vh - 150px);word-wrap:break-word;color:{1};padding:0}}
#body .ProseMirror p{{margin:0 0 8px 0}}
#body .ProseMirror h1{{font-size:24px;font-weight:600;margin:16px 0 8px}}
#body .ProseMirror h2{{font-size:20px;font-weight:600;margin:14px 0 8px}}
#body .ProseMirror h3{{font-size:17px;font-weight:600;margin:12px 0 6px}}
#body .ProseMirror ul,#body .ProseMirror ol{{margin:0 0 8px 0;padding-left:24px}}
#body .ProseMirror pre{{background:rgba(0,0,0,0.2);padding:10px 14px;border-radius:8px;font-family:Consolas,Monaco,monospace;font-size:13px;white-space:pre-wrap;margin:8px 0}}
#body .ProseMirror code{{font-family:Consolas,Monaco,monospace}}
#body .ProseMirror blockquote{{border-left:3px solid {3};margin:8px 0;padding-left:14px;color:{2}}}
#body .ProseMirror hr{{border:none;border-top:1px solid {3};margin:16px 0}}
#body .ProseMirror a{{color:#8C93FF;text-decoration:underline;cursor:pointer}}
img{{max-width:100%;height:auto;display:block;margin:4px 0}}
</style></head><body>
<div id='title' contenteditable='true' spellcheck='false'></div>
<div class='sep'></div>
<div id='body'></div>
<script>{8}</script>
<script>
var tel=document.getElementById('title');
var bel=document.getElementById('body');
var unlinkLabel='{9}';
var T=window.NovaraTiptap;
var editor=new T.Editor({{
  element:bel,
  extensions:[
    T.StarterKit,
    T.TextStyle,
    T.Color,
    T.Image,
    T.Underline,
    T.Link.configure({{openOnClick:false,autolink:true,HTMLAttributes:{{target:'_blank',rel:'noopener noreferrer nofollow'}}}}),
    T.TextAlign.configure({{types:['heading','paragraph','image']}})
  ],
  content:'',
  onUpdate:function(){{}},
  onSelectionUpdate:function(){{notifyFormatState()}}
}});
function notifyFormatState(){{window.chrome.webview.postMessage(JSON.stringify({{action:'formatState',bold:editor.isActive('bold'),italic:editor.isActive('italic'),underline:editor.isActive('underline')}}))}}
function execBold(){{editor.chain().focus().toggleBold().run();notifyFormatState()}}
function execItalic(){{editor.chain().focus().toggleItalic().run();notifyFormatState()}}
function execUnderline(){{editor.chain().focus().toggleUnderline().run();notifyFormatState()}}
function execForeColor(c){{editor.chain().focus().setColor(c).run()}}
function execClear(){{editor.chain().focus().unsetAllMarks().clearNodes().run();notifyFormatState()}}
function execUndo(){{editor.chain().focus().undo().run();notifyFormatState()}}
function execRedo(){{editor.chain().focus().redo().run();notifyFormatState()}}
function execAlign(a){{editor.chain().focus().setTextAlign(a).run()}}
function execCodeBlock(){{editor.chain().focus().toggleCodeBlock().run()}}
function execHorizontalRule(){{editor.chain().focus().setHorizontalRule().run()}}
function insertImage(b64,name,mime){{editor.chain().focus().setImage({{src:'data:'+(mime||'image/png')+';base64,'+b64,alt:name||''}}).run()}}
function setAll(ht,hb){{tel.textContent=ht||'';editor.commands.setContent(hb||'');updateTitleSpacing()}}
function updateTitleSpacing(){{var t=tel.textContent||'';var sp='0.18em';for(var i=0;i<t.length;i++){{var c=t.charCodeAt(i);if((c>=65&&c<=90)||(c>=97&&c<=122)||(c>=0xAC00&&c<=0xD7AF)||(c>=0x1100&&c<=0x11FF)||(c>=0x3040&&c<=0x30FF)){{sp='0';break}}}}tel.style.letterSpacing=sp}}
tel.addEventListener('input',function(){{updateTitleSpacing()}});
function getTitle(){{return tel.innerHTML}}
function getBody(){{return editor.getHTML()}}
bel.addEventListener('click',function(e){{var a=e.target&&e.target.closest?e.target.closest('a'):null;if(a){{e.preventDefault();var href=a.getAttribute('href');if(href)window.chrome.webview.postMessage(JSON.stringify({{action:'openLink',url:href}}))}}}});
var ctxMenu=document.createElement('div');
ctxMenu.style.cssText='position:fixed;z-index:9999;background:{10};border:1px solid {3};border-radius:8px;padding:4px 0;box-shadow:0 4px 16px rgba(0,0,0,0.3);display:none;';
var unlinkItem=document.createElement('div');
unlinkItem.textContent=unlinkLabel;
unlinkItem.style.cssText='padding:8px 16px;font-size:13px;color:{1};cursor:pointer;white-space:nowrap;';
unlinkItem.onmouseenter=function(){{unlinkItem.style.background='{4}'}};
unlinkItem.onmouseleave=function(){{unlinkItem.style.background='transparent'}};
unlinkItem.onclick=function(){{editor.chain().focus().unsetLink().run();hideCtx()}};
ctxMenu.appendChild(unlinkItem);
document.body.appendChild(ctxMenu);
function showCtx(x,y){{ctxMenu.style.left=x+'px';ctxMenu.style.top=y+'px';ctxMenu.style.display='block'}}
function hideCtx(){{ctxMenu.style.display='none'}}
bel.addEventListener('contextmenu',function(e){{var a=e.target&&e.target.closest?e.target.closest('a'):null;if(a){{e.preventDefault();showCtx(e.clientX,e.clientY)}}else{{hideCtx()}}}});
document.addEventListener('click',function(e){{if(!ctxMenu.contains(e.target))hideCtx()}});
window.chrome.webview.addEventListener('message',function(e){{try{{var m=JSON.parse(e.data);switch(m.action){{case'setAll':setAll(m.title||'',m.body||'');break;case'insertImage':insertImage(m.base64,m.filename,m.mime);break;case'execBold':execBold();break;case'execItalic':execItalic();break;case'execUnderline':execUnderline();break;case'execForeColor':execForeColor(m.color);break;case'execClear':execClear();break;case'execUndo':execUndo();break;case'execRedo':execRedo();break;case'execAlign':execAlign(m.align);break;case'execCodeBlock':execCodeBlock();break;case'execHorizontalRule':execHorizontalRule();break;}}}}catch(err){{}}}});
tel.addEventListener('keydown',function(e){{var t=tel.textContent.replace(/\s/g,'');if(t.length>=120&&e.key.length===1&&!e.ctrlKey&&!e.metaKey&&!e.isComposing&&e.keyCode!==229){{e.preventDefault()}}}});tel.addEventListener('drop',function(e){{if(e.target===tel||tel.contains(e.target))e.preventDefault()}});
tel.addEventListener('paste',function(e){{e.preventDefault();var txt=(e.clipboardData||window.clipboardData).getData('text/plain')||'';var sel=window.getSelection();if(sel&&sel.rangeCount&&tel.contains(sel.anchorNode)){{try{{sel.deleteFromDocument()}}catch(err){{}}}}var t=tel.textContent.replace(/\s/g,'');var rem=120-t.length;if(rem<=0)return;var out='',ns=0;for(var i=0;i<txt.length;i++){{var ch=txt.charAt(i);out+=ch;if(!/\s/.test(ch)){{ns++;if(ns>=rem)break}}}}document.execCommand('insertText',false,out)}});
notifyFormatState();
</script></body></html>";

    private string GetEditorHtml()
    {

        bool isLight = App.CurrentTheme == "浅色模式"
            || (App.MainWindow?.Content is FrameworkElement root && root.ActualTheme == ElementTheme.Light);
        string bg = isLight ? "#F2F2F2" : "#1E1E1E";
        string text = isLight ? "rgba(0,0,0,0.87)" : "rgba(255,255,255,0.87)";
        string placeholder = isLight ? "rgba(0,0,0,0.35)" : "rgba(255,255,255,0.4)";
        string sep = isLight ? "rgba(0,0,0,0.12)" : "rgba(255,255,255,0.2)";
        string selection = isLight ? "rgba(0,0,0,0.15)" : "rgba(255,255,255,0.2)";
        string scrollbar = isLight ? "rgba(0,0,0,0.15)" : "rgba(255,255,255,0.15)";
        string menuBg = isLight ? "#FFFFFF" : "#2A2A2A";
        // 4.0 #4: Tiptap bundle is inlined into the template (offline, no CDN). It has no raw </script>
        // so it is safe to inline; read from the publish dir (copied by csproj).
        string bundle;
        try { bundle = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "tiptap.bundle.js")); }
        catch { bundle = "window.NovaraTiptap={};"; }
        return string.Format(EditorHtmlTemplate, bg, text, placeholder, sep, selection, scrollbar,
            App.GetString("DiaryEditor_TitlePlaceholder"), App.GetString("DiaryEditor_BodyPlaceholder"),
            bundle, App.GetString("Menu_Unlink"), menuBg);
    }

    
    private const string MarkdownHtmlTemplate = @"<!DOCTYPE html>
<html><head><meta charset='utf-8'><style>
*{{margin:0;padding:0;box-sizing:border-box}}
html,body{{background:{0};font-family:'Segoe UI',sans-serif;color:{1}}}
body{{overflow-y:auto;color:{1};padding-bottom:80px}}
body::-webkit-scrollbar{{width:6px}}body::-webkit-scrollbar-track{{background:transparent}}body::-webkit-scrollbar-thumb{{background:{4};border-radius:3px}}
#title{{font-size:17px;font-weight:normal;letter-spacing:0.18em;line-height:26px;outline:none;padding:4px 0 8px 0;word-wrap:break-word;color:{1}}}
#title:empty::before{{content:'{2}';color:{3};font-weight:normal;letter-spacing:0.18em}}
.sep{{height:1px;background:{5};margin:0 0 16px 0;opacity:0.45}}
#md-editor{{width:100%;min-height:calc(100vh - 150px);background:transparent;border:none;outline:none;resize:none;overflow-y:hidden;color:{1};font-family:Consolas,Monaco,'Courier New',monospace;font-size:14px;line-height:22px;padding:0}}
#md-preview{{display:none;min-height:calc(100vh - 150px);font-size:14px;line-height:22px;color:{1};word-wrap:break-word}}
#md-preview h1{{font-size:24px;font-weight:600;margin:16px 0 8px}}
#md-preview h2{{font-size:20px;font-weight:600;margin:14px 0 8px}}
#md-preview h3{{font-size:17px;font-weight:600;margin:12px 0 6px}}
#md-preview p{{margin:0 0 8px 0}}
#md-preview ul,#md-preview ol{{margin:0 0 8px 0;padding-left:24px}}
#md-preview pre{{background:rgba(0,0,0,0.2);padding:10px 14px;border-radius:8px;font-family:Consolas,Monaco,monospace;font-size:13px;white-space:pre-wrap;margin:8px 0}}
#md-preview code{{font-family:Consolas,Monaco,monospace}}
#md-preview blockquote{{border-left:3px solid {5};margin:8px 0;padding-left:14px;color:{3}}}
#md-preview hr{{border:none;border-top:1px solid {5};margin:16px 0}}
#md-preview a{{color:#8C93FF;text-decoration:underline}}
#md-preview img{{max-width:100%;height:auto}}
</style></head><body>
<div id='title' contenteditable='true' spellcheck='false'></div>
<div class='sep'></div>
<textarea id='md-editor' spellcheck='false'></textarea>
<div id='md-preview'></div>
<script>{6}</script>
<script>
var tel=document.getElementById('title');
var ta=document.getElementById('md-editor');
var pv=document.getElementById('md-preview');
function setMd(t,b){{tel.textContent=t||'';ta.value=b||'';updateTitleSpacing();autoResize();renderPreview()}}
function autoResize(){{var doc=document.documentElement;var prev=doc.scrollTop;ta.style.height='auto';ta.style.height=ta.scrollHeight+'px';if(doc.scrollTop!==prev)doc.scrollTop=prev}}
function renderPreview(){{pv.innerHTML=window.NovaraMd.render(ta.value)}}
function getTitle(){{return tel.innerHTML}}
function getMd(){{return ta.value}}
function showWrite(){{ta.style.display='block';pv.style.display='none';autoResize()}}
function showPreview(){{renderPreview();ta.style.display='none';pv.style.display='block'}}
function wrapSel(before,after,ph){{var s=ta.selectionStart,e=ta.selectionEnd;var sel=ta.value.substring(s,e)||ph;ta.setRangeText(before+sel+after,s,e,'end');ta.setSelectionRange(s+before.length,s+before.length+sel.length);ta.focus()}}
function prefixLines(prefix){{var s=ta.selectionStart;var val=ta.value;var ls=val.lastIndexOf('\n',s-1)+1;ta.setRangeText(prefix,ls,ls,'end');ta.focus()}}
function insertBlock(text){{var s=ta.selectionStart,e=ta.selectionEnd;ta.setRangeText(text,s,e,'end');ta.focus()}}
function mdBold(){{wrapSel('**','**','bold')}}
function mdItalic(){{wrapSel('*','*','italic')}}
function mdStrike(){{wrapSel('~~','~~','text')}}
function mdInlineCode(){{wrapSel('`','`','code')}}
function mdHeading(n){{prefixLines('#'.repeat(n)+' ')}}
function mdBullet(){{prefixLines('- ')}}
function mdOrdered(){{prefixLines('1. ')}}
function mdQuote(){{prefixLines('> ')}}
function mdLink(){{wrapSel('[','](url)','text')}}
function mdImage(){{wrapSel('![','](url)','alt')}}
function mdCodeBlock(lang){{var s=ta.selectionStart,e=ta.selectionEnd;var sel=ta.value.substring(s,e);ta.setRangeText('```'+(lang||'')+'\n'+sel+'\n```',s,e,'end');ta.focus()}}
function mdHr(){{insertBlock('\n\n---\n\n')}}
function mdTable(){{insertBlock('|  |  |  |\n|---|---|---|\n|  |  |  |\n|  |  |  |\n')}}
function mdClearFormat(){{var s=ta.selectionStart,e=ta.selectionEnd;var sel=ta.value.substring(s,e);ta.setRangeText(sel.replace(/[*_~`>#]/g,''),s,e,'end');ta.focus()}}
function mdUndo(){{ta.focus();document.execCommand('undo')}}
function mdRedo(){{ta.focus();document.execCommand('redo')}}
function updateTitleSpacing(){{var t=tel.textContent||'';var sp='0.18em';for(var i=0;i<t.length;i++){{var c=t.charCodeAt(i);if((c>=65&&c<=90)||(c>=97&&c<=122)||(c>=0xAC00&&c<=0xD7AF)||(c>=0x1100&&c<=0x11FF)||(c>=0x3040&&c<=0x30FF)){{sp='0';break}}}}tel.style.letterSpacing=sp}}
tel.addEventListener('input',function(){{updateTitleSpacing()}});
ta.addEventListener('input',function(){{autoResize()}});
tel.addEventListener('keydown',function(e){{var t=tel.textContent.replace(/\s/g,'');if(t.length>=120&&e.key.length===1&&!e.ctrlKey&&!e.metaKey&&!e.isComposing&&e.keyCode!==229){{e.preventDefault()}}}});
tel.addEventListener('drop',function(e){{if(e.target===tel||tel.contains(e.target))e.preventDefault()}});
tel.addEventListener('paste',function(e){{e.preventDefault();var txt=(e.clipboardData||window.clipboardData).getData('text/plain')||'';var sel=window.getSelection();if(sel&&sel.rangeCount&&tel.contains(sel.anchorNode)){{try{{sel.deleteFromDocument()}}catch(err){{}}}}var t=tel.textContent.replace(/\s/g,'');var rem=120-t.length;if(rem<=0)return;var out='',ns=0;for(var i=0;i<txt.length;i++){{var ch=txt.charAt(i);out+=ch;if(!/\s/.test(ch)){{ns++;if(ns>=rem)break}}}}document.execCommand('insertText',false,out)}});
window.chrome.webview.addEventListener('message',function(e){{try{{var m=JSON.parse(e.data);switch(m.action){{case'setMd':setMd(m.title||'',m.body||'');break;case'showWrite':showWrite();break;case'showPreview':showPreview();break;case'mdBold':mdBold();break;case'mdItalic':mdItalic();break;case'mdStrike':mdStrike();break;case'mdInlineCode':mdInlineCode();break;case'mdHeading':mdHeading(m.level||1);break;case'mdBullet':mdBullet();break;case'mdOrdered':mdOrdered();break;case'mdQuote':mdQuote();break;case'mdLink':mdLink();break;case'mdImage':mdImage();break;case'mdCodeBlock':mdCodeBlock(m.lang||'');break;case'mdHr':mdHr();break;case'mdTable':mdTable();break;case'mdClearFormat':mdClearFormat();break;case'mdUndo':mdUndo();break;case'mdRedo':mdRedo();break;}}}}catch(err){{}}}});
</script></body></html>";

    private string GetMarkdownHtml()
    {
        bool isLight = App.CurrentTheme == "浅色模式"
            || (App.MainWindow?.Content is FrameworkElement root && root.ActualTheme == ElementTheme.Light);
        string bg = isLight ? "#F2F2F2" : "#1E1E1E";
        string text = isLight ? "rgba(0,0,0,0.87)" : "rgba(255,255,255,0.87)";
        string placeholder = isLight ? "rgba(0,0,0,0.35)" : "rgba(255,255,255,0.4)";
        string sep = isLight ? "rgba(0,0,0,0.12)" : "rgba(255,255,255,0.2)";
        string scrollbar = isLight ? "rgba(0,0,0,0.15)" : "rgba(255,255,255,0.15)";
        string bundle;
        try { bundle = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "md.bundle.js")); }
        catch { bundle = "window.NovaraMd={render:function(t){return (t||'').replace(/</g,'&lt;')}};"; }
        return string.Format(MarkdownHtmlTemplate, bg, text, App.GetString("DiaryEditor_DocumentTitlePlaceholder"), placeholder, scrollbar, sep, bundle);
    }

    
    
    internal static string EnforceTitleLength(string title)
    {
        if (string.IsNullOrEmpty(title)) return title;
        int nonSpace = 0;
        foreach (var c in title) if (!char.IsWhiteSpace(c)) nonSpace++;
        if (nonSpace <= 120) return title;

        var sb = new System.Text.StringBuilder();
        int kept = 0;
        foreach (var c in title)
        {
            if (!char.IsWhiteSpace(c))
            {
                if (kept >= 120) break;
                kept++;
            }
            sb.Append(c);
        }
        return sb.ToString().Trim();
    }

    public DiaryEditorPage()
    {
        InitializeComponent();
        Novara.Services.DialogDepth.AttachContainer((Grid)Content, autoVeil: true); 
        Loaded += Page_Loaded;
        Unloaded += Page_Unloaded;
        KeyDown += Page_KeyDown; // N5D-07: Esc closes the three toolbar picker panels (U2 parity)
    }

    private void Page_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape) return;
        bool any = _isColorPickerOpen || _isAlignPickerOpen || _isMdHeadingPickerOpen;
        if (!any) return;
        if (_isColorPickerOpen) HideColorPickerPanel();
        if (_isAlignPickerOpen) HideAlignPickerPanel();
        if (_isMdHeadingPickerOpen) HideMdHeadingPickerPanel();
        e.Handled = true;
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        
        
        
    }

    private void OnNavigationCompleted(Microsoft.UI.Xaml.Controls.WebView2 sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess) { _webViewReady = false; return; } 
        _navigatingFormat = null; // N4D-02: the in-flight template has landed and matches _loadedFormat
        _webViewReady = true;
        SetDiaryContent(_currentDiary);
        UpdateToolbarForFormat();
        UpdateMdViewSwitch(true); 
    }

    private void OnWebMessageReceived(Microsoft.Web.WebView2.Core.CoreWebView2 sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            var json = args.TryGetWebMessageAsString();
            if (string.IsNullOrEmpty(json)) return;
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("action", out var actEl)) return;
            var action = actEl.GetString();
            if (action == "formatState")
            {
                var msg = JsonSerializer.Deserialize<FormatStateMessage>(json, JsonCaseInsensitive);
                if (msg == null) return;
                _isBold = msg.Bold;
                _isItalic = msg.Italic;
                _isUnderline = msg.Underline;
                UpdateFormatButtonStates();
            }
            else if (action == "openLink")
            {
                var url = doc.RootElement.TryGetProperty("url", out var u) ? u.GetString() : null;
                if (!string.IsNullOrWhiteSpace(url)) OpenExternalLink(url);
            }
        }
        catch { }
    }

    private static void OpenExternalLink(string url)
    {
        var target = url.Trim();
        if (!target.Contains("://")) target = "https://" + target;
        // NH3 (defense-in-depth): only schemes the HtmlSanitizer whitelist allows may reach
        // ShellExecute. file://, javascript:, ms-* etc. are dropped even if a future regression
        // ever lets one through the content layer.
        var lower = target.ToLowerInvariant();
        if (!(lower.StartsWith("http://") || lower.StartsWith("https://") || lower.StartsWith("mailto:") || lower.StartsWith("ftp://")))
        {
            System.Diagnostics.Debug.WriteLine($"OpenExternalLink 已拒绝非白名单协议: {target}");
            return;
        }
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true }); }
        catch { }
    }

    private sealed class FormatStateMessage
    {
        public string Action { get; set; } = "";
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public bool Underline { get; set; }
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        
        
        if (_webViewReady) return;
        try
        {
            
            
            var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateWithOptionsAsync(
                null,
                System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    Novara.Services.CoreEnv.DataDirName, "Webview2"),
                null);
            await ContentWebView.EnsureCoreWebView2Async(env);
            BackPathIcon.Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), IconData.Back);
            InitializeToolbarIcons();
            var cv = ContentWebView.CoreWebView2;
            if (cv == null) return;
            cv.Settings.IsScriptEnabled = true;
            cv.Settings.AreDefaultScriptDialogsEnabled = false;
            cv.Settings.IsWebMessageEnabled = true;

            cv.WebMessageReceived -= OnWebMessageReceived;
            cv.WebMessageReceived += OnWebMessageReceived;

            ContentWebView.NavigationCompleted -= OnNavigationCompleted;
            ContentWebView.NavigationCompleted += OnNavigationCompleted;

            ContentWebView.NavigateToString(_loadedFormat == "markdown" ? GetMarkdownHtml() : GetEditorHtml());
            _navigatingFormat = _loadedFormat; // N4D-02: remember which template is in flight
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"日记编辑器初始化失败: {ex}");
        }
    }

    public void LoadDiary(DiaryEntry? diary, string? newFormat = null)
    {
        BackButton.IsEnabled = true; // N4D-03: re-arm the back button for this editing session
        _currentDiary = diary;
        _currentImageBytes = diary != null ? EstimateImageBytes(diary.Content) : 0; 
        var format = diary?.Format ?? newFormat ?? "html";
        if (diary != null)
        {
            _initialTitle = (diary.Title == "(无标题)" || diary.Title == App.GetString("DiaryEditor_Untitled")) ? "" : diary.Title;
            _initialContent = format == "markdown" ? diary.Content : HtmlSanitizer.Sanitize(diary.Content);
        }
        else
        {
            _initialTitle = null;
            _initialContent = null;
        }

        if (_webViewReady && _loadedFormat != format)
        {
            
            _loadedFormat = format;
            _webViewReady = false;
            ContentWebView.NavigateToString(format == "markdown" ? GetMarkdownHtml() : GetEditorHtml());
            _navigatingFormat = format; // N4D-02
        }
        else if (_webViewReady)
        {
            SetDiaryContent(diary);
        }
        else if (_navigatingFormat != null && _navigatingFormat != format)
        {
            // N4D-02: a template navigation for another format is still in flight - restart it for this
            // format. Otherwise the landing handler would post setAll/setMd per the NEW _loadedFormat into
            // the OLD bundle, get silently dropped (PostMessageAsync), and leave an A-template + B-format
            // blank editor stuck until another format switch.
            _loadedFormat = format;
            ContentWebView.NavigateToString(format == "markdown" ? GetMarkdownHtml() : GetEditorHtml());
            _navigatingFormat = format;
        }
        else
        {
            
            _loadedFormat = format;
        }
        ResetFormatStates();
    }

    
    private void SetDiaryContent(DiaryEntry? diary)
    {
        if (diary == null) return;
        string title = (diary.Title == "(无标题)" || diary.Title == App.GetString("DiaryEditor_Untitled")) ? "" : diary.Title;
        if (_loadedFormat == "markdown")
            PostMessageAsync("setMd", new { title = title, body = diary.Content });
        else
            PostMessageAsync("setAll", new { title = title, body = HtmlSanitizer.Sanitize(diary.Content) });
    }

    /// <summary>Estimate the total decoded byte size of base64 images embedded in the stored HTML
    
    private static long EstimateImageBytes(string html)
    {
        if (string.IsNullOrEmpty(html)) return 0;
        long total = 0;
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(html, @"data:image/[^;""']*;base64,([A-Za-z0-9+/=]+)"))
            total += (long)(m.Groups[1].Value.Length * 3 / 4);
        return total;
    }

    
    
    public void ClearContent()
    {
        try
        {
            if (_webViewReady) PostMessageAsync(_loadedFormat == "markdown" ? "setMd" : "setAll", new { title = "", body = "" });
        }
        catch { }
        _currentDiary = null;
        _currentImageBytes = 0;
        _initialTitle = null;
        _initialContent = null;
        BackButton.IsEnabled = true; // N4D-03: fade-out finished - the editor is idle again
        ResetFormatStates();
    }

    
    
    public void Shutdown()
    {
        _webViewReady = false;
        _navigatingFormat = null; // N4D-02
        try { ContentWebView.Close(); } catch { }
        _currentDiary = null;
        _currentImageBytes = 0;
        _initialTitle = null;
        _initialContent = null;
    }

    // ================================================================

    // ================================================================

    private void PostMessageAsync(string action, object? data = null)
    {
        try
        {
            var cv = ContentWebView.CoreWebView2;
            if (cv == null || !_webViewReady) { System.Diagnostics.Debug.WriteLine($"PostMessage 丢弃（WebView2 未就绪）: {action}"); return; }
            var msg = new Dictionary<string, object> { ["action"] = action };
            if (data != null)
                foreach (var p in data.GetType().GetProperties())
                    msg[p.Name] = p.GetValue(data)!;
            cv.PostWebMessageAsString(JsonSerializer.Serialize(msg));
        }
        catch { }
    }

    private async System.Threading.Tasks.Task<string?> GetJsStringAsync(string script)
    {
        try
        {
            if (ContentWebView.CoreWebView2 == null) return null;
            var json = await ContentWebView.CoreWebView2.ExecuteScriptAsync(script);
            // NH2: no ?? "" here - a JSON literal "null" (the script threw) must stay null so the
            // save guard treats it as failure; coercing to "" made it indistinguishable from real
            // empty content and let a blank overwrite through.
            return JsonSerializer.Deserialize<string>(json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"执行 JS 失败: {ex}");
            return null; // C5 (Round 5): JS failure returns null (vs empty string); save aborts to avoid blank overwrite
        }
    }

    // ================================================================

    // ================================================================

    private async void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (!BackButton.IsEnabled) return; // N4D-03: swallow rapid double-clicks while the first navigation's fade-out is still running
        BackButton.IsEnabled = false;
        bool wasNew = _currentDiary == null;
        bool saved = await SaveCurrentDiaryAsync();
        if (saved) App.ShowToast(App.GetString(wasNew ? "Common_Toast_Created" : "Common_Toast_Modified"));
        App.MainWindow?.NavigateBackFromEditor(); // re-enabled by ClearContent/LoadDiary when the editor is next used
    }

    public async System.Threading.Tasks.Task<bool> SaveCurrentDiaryAsync()
    {
        if (!_webViewReady)
        {

            System.Diagnostics.Debug.WriteLine("日记保存被阻止：WebView2 未就绪");
            return false;
        }
        string? rawTitle = await GetJsStringAsync("getTitle()");
        bool isMd = _loadedFormat == "markdown";
        string? rawBody = await GetJsStringAsync(isMd ? "getMd()" : "getBody()");
        if (rawTitle == null || rawBody == null)
        {
            // C5 (Round 5): JS failed (navigating/destroyed) - abort save to prevent blank overwrite
            System.Diagnostics.Debug.WriteLine("日记保存被阻止：JS 执行失败，内容未变更");
            return false;
        }

        string plainTitle = System.Text.RegularExpressions.Regex.Replace(rawTitle, "<.*?>", "").Trim();
        plainTitle = System.Web.HttpUtility.HtmlDecode(plainTitle);
        plainTitle = EnforceTitleLength(plainTitle); 
        string cleanedBody = isMd ? rawBody : HtmlSanitizer.Sanitize(rawBody);
        bool bodyHasContent;
        if (isMd)
            bodyHasContent = !string.IsNullOrWhiteSpace(rawBody); 
        else
            
            // (or any non-empty text) as "has content"; an empty paragraph <p></p> counts as empty.
            bodyHasContent = cleanedBody.Contains("<img", StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrWhiteSpace(System.Text.RegularExpressions.Regex.Replace(cleanedBody, "<.*?>", "").Trim());

        if (_currentDiary == null && string.IsNullOrEmpty(plainTitle) && !bodyHasContent)
            return false;

        
        if (_currentDiary != null && plainTitle == _initialTitle && cleanedBody == _initialContent)
            return false;

        var entry = _currentDiary ?? new DiaryEntry();
        if (_currentDiary == null && isMd) entry.Format = "markdown"; 
        entry.Title = string.IsNullOrEmpty(plainTitle) ? App.GetString("DiaryEditor_Untitled") : plainTitle;
        entry.Content = cleanedBody;

        if (_currentDiary == null)
        {
            entry.CreatedAt = DateTime.Now;
            entry.ModifiedAt = entry.CreatedAt;
        }
        else
        {
            entry.ModifiedAt = DateTime.Now;
        }

        App.MainWindow?.UpsertDiary(entry);
        // N4W-02 companion: snapshot the just-saved state so repeat invocations (Closing after ExitApp's
        // pre-save, rapid double-back) hit the dirty check above instead of re-upserting - and a brand-new
        // diary is no longer duplicated by a second call while _currentDiary is still null.
        _currentDiary = entry;
        _initialTitle = plainTitle;
        _initialContent = cleanedBody;
        return true;
    }

    // ================================================================
    //  B / I / U
    // ================================================================

    /* ========== DiaryEditor B/I/U Toolbar ==========
Function: Bold/italic/underline buttons: execCommand + format-state feedback, selectionchange-driven active state, floating capsule toolbar
Corresponding UI: DiaryEditorPage.xaml.cs
Logic Range: Below methods in this region
*/
private void BoldButton_Click(object sender, RoutedEventArgs e)
    {
        PostMessageAsync("execBold");
    }

    private void ItalicButton_Click(object sender, RoutedEventArgs e)
    {
        PostMessageAsync("execItalic");
    }

    private void UnderlineButton_Click(object sender, RoutedEventArgs e)
    {
        PostMessageAsync("execUnderline");
    }

    private void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        PostMessageAsync("execUndo");
    }

    private void RedoButton_Click(object sender, RoutedEventArgs e)
    {
        PostMessageAsync("execRedo");
    }

    private void AlignButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isAlignPickerOpen) { HideAlignPickerPanel(); return; }
        HideColorPickerPanel(); // only one picker may be open at a time
        ShowAlignPickerPanel();
    }

    private void AlignItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string align)
            PostMessageAsync("execAlign", new { align });
        HideAlignPickerPanel();
    }

    private void CodeBlockButton_Click(object sender, RoutedEventArgs e)
    {
        PostMessageAsync("execCodeBlock");
    }

    private void HorizontalRuleButton_Click(object sender, RoutedEventArgs e)
    {
        PostMessageAsync("execHorizontalRule");
    }

    private void ShowAlignPickerPanel()
    {
        _isAlignPickerOpen = true;
        var t = AlignButton.TransformToVisual(ToolbarContainer);
        var p = t.TransformPoint(new Windows.Foundation.Point(20, 0));
        var width = ToolbarContainer.ActualWidth;
        AlignPickerTranslate.X = width > 0 ? p.X - width / 2 : p.X;
        AlignPickerScrim.Visibility = Visibility.Visible;
        AlignPickerPanel.Visibility = Visibility.Visible; AlignPickerPanel.Opacity = 0; AlignPickerTranslate.Y = -12;
        var sb = new Storyboard();
        var fi = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(230), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(fi, AlignPickerPanel); Storyboard.SetTargetProperty(fi, "Opacity"); sb.Children.Add(fi);
        var si = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(260), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(si, AlignPickerPanel); Storyboard.SetTargetProperty(si, "(UIElement.RenderTransform).(TranslateTransform.Y)"); sb.Children.Add(si);
        sb.Begin();
    }

    private void HideAlignPickerPanel()
    {
        if (!_isAlignPickerOpen) return;
        _isAlignPickerOpen = false;
        AlignPickerScrim.Visibility = Visibility.Collapsed;
        var sb = new Storyboard();
        var fo = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(180), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(fo, AlignPickerPanel); Storyboard.SetTargetProperty(fo, "Opacity"); sb.Children.Add(fo);
        var so = new DoubleAnimation { To = -12, Duration = TimeSpan.FromMilliseconds(200), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(so, AlignPickerPanel); Storyboard.SetTargetProperty(so, "(UIElement.RenderTransform).(TranslateTransform.Y)"); sb.Children.Add(so);
        sb.Completed += (_, _) => { AlignPickerPanel.Visibility = Visibility.Collapsed; }; sb.Begin();
    }

    private void AlignPickerScrim_Tapped(object sender, TappedRoutedEventArgs e)
    {
        HideAlignPickerPanel();
    }

    private void UpdateFormatButtonStates()
    {
        
        
        var activeBg = App.GetBrush("AppPrimaryButtonBrush");   
        var normalBg = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)); 
        var white = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
        var iconBrush = App.GetBrush("IconForegroundBrush");

        BoldButton.Background = _isBold ? activeBg : normalBg;
        BoldPathIcon.Fill = _isBold ? white : iconBrush;
        VisualStateManager.GoToState(BoldButton, _isBold ? "Pressed" : "Normal", true);
        ItalicButton.Background = _isItalic ? activeBg : normalBg;
        ItalicPathIcon.Fill = _isItalic ? white : iconBrush;
        VisualStateManager.GoToState(ItalicButton, _isItalic ? "Pressed" : "Normal", true);
        UnderlineButton.Background = _isUnderline ? activeBg : normalBg;
        UnderlinePathIcon.Fill = _isUnderline ? white : iconBrush;
        VisualStateManager.GoToState(UnderlineButton, _isUnderline ? "Pressed" : "Normal", true);
    }

    private void ResetFormatStates()
    {
        _isBold = _isItalic = _isUnderline = false;
        // NH9: per-document defaults - a reused editor instance must not inherit the previous
        // document's Preview mode or collapsed toolbar.
        _isMdPreview = false;
        
        
        if (_webViewReady)
        {
            if (_loadedFormat == "markdown") { PostMessageAsync("showWrite"); UpdateMdViewSwitch(true); ExpandMdToolbar(); }
            else ExpandHtmlToolbar(); // N4D-01: was unconditional ExpandMdToolbar - an html doc whose toolbar was folded came back showing the MD toolbar
            UpdateFormatButtonStates();
        }
        else _isToolbarCollapsed = false;
    }

    // ================================================================

    // ================================================================

        
        
        private static (double Dx, double Dy, double Sx, double Sy) MeltMetrics(Grid container)
        {
            var parent = container.Parent as FrameworkElement;
            double pw = parent?.ActualWidth ?? 0, ph = parent?.ActualHeight ?? 0;
            if (pw <= 0 || ph <= 0) return (0, 0, 1, 1);
            var tl = container.TransformToVisual(parent).TransformPoint(new Windows.Foundation.Point(0, 0));
            double cx = tl.X + container.ActualWidth / 2.0, cy = tl.Y + container.ActualHeight / 2.0;
            double dx = (pw - 46.0) - cx, dy = (ph - 46.0) - cy;
            double s = System.Math.Max(0.05, System.Math.Min(1.0, 52.0 / System.Math.Max(container.ActualWidth, 1)));
            return (dx, dy, s, s);
        }

        private void MeltToolbarAway(Grid container, Microsoft.UI.Xaml.Media.CompositeTransform tr)
        {
            if (_isToolbarCollapsed) return;
            HideAlignPickerPanel();
            _isToolbarCollapsed = true;
            ExpandButton.Visibility = Visibility.Collapsed; 
            var (dx, dy, scx, scy) = MeltMetrics(container);
            tr.CenterX = container.ActualWidth / 2.0; tr.CenterY = container.ActualHeight / 2.0;
            var sb = new Storyboard();
            var tx = new DoubleAnimation { To = dx, Duration = TimeSpan.FromMilliseconds(340), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(tx, tr); Storyboard.SetTargetProperty(tx, "TranslateX"); sb.Children.Add(tx);
            var ty = new DoubleAnimation { To = dy, Duration = TimeSpan.FromMilliseconds(340), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(ty, tr); Storyboard.SetTargetProperty(ty, "TranslateY"); sb.Children.Add(ty);
            var sx = new DoubleAnimation { To = scx, Duration = TimeSpan.FromMilliseconds(340), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(sx, tr); Storyboard.SetTargetProperty(sx, "ScaleX"); sb.Children.Add(sx);
            var sy = new DoubleAnimation { To = scy, Duration = TimeSpan.FromMilliseconds(340), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(sy, tr); Storyboard.SetTargetProperty(sy, "ScaleY"); sb.Children.Add(sy);
            var f = new DoubleAnimation { To = 0, BeginTime = TimeSpan.FromMilliseconds(170), Duration = TimeSpan.FromMilliseconds(170), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(f, container); Storyboard.SetTargetProperty(f, "Opacity"); sb.Children.Add(f);
            sb.Completed += (_, _) => { container.Visibility = Visibility.Collapsed; tr.TranslateX = 0; tr.TranslateY = 0; tr.ScaleX = 1; tr.ScaleY = 1; container.Opacity = 1; ShowExpandButton(); };
            sb.Begin();
        }

        private void MeltToolbarBack(Grid container, Microsoft.UI.Xaml.Media.CompositeTransform tr)
        {
            ExpandButton.Visibility = Visibility.Collapsed; 
            var (dx, dy, scx, scy) = MeltMetrics(container);
            tr.CenterX = container.ActualWidth / 2.0; tr.CenterY = container.ActualHeight / 2.0;
            tr.TranslateX = dx; tr.TranslateY = dy; tr.ScaleX = scx; tr.ScaleY = scy;
            container.Opacity = 0; container.Visibility = Visibility.Visible;
            var sb = new Storyboard();
            var tx = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(320), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(tx, tr); Storyboard.SetTargetProperty(tx, "TranslateX"); sb.Children.Add(tx);
            var ty = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(320), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(ty, tr); Storyboard.SetTargetProperty(ty, "TranslateY"); sb.Children.Add(ty);
            var sxa = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(320), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(sxa, tr); Storyboard.SetTargetProperty(sxa, "ScaleX"); sb.Children.Add(sxa);
            var sya = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(320), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(sya, tr); Storyboard.SetTargetProperty(sya, "ScaleY"); sb.Children.Add(sya);
            var f = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(220), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(f, container); Storyboard.SetTargetProperty(f, "Opacity"); sb.Children.Add(f);
            sb.Begin();
        }

    private void CollapseButton_Click(object sender, RoutedEventArgs e)
        => MeltToolbarAway(ToolbarContainer, ToolbarTranslate);

    private void ShowExpandButton()
    {
        InitializeExpandIcon();
        ExpandButton.Visibility = Visibility.Visible; ExpandButton.Opacity = 0;
        ExpandButton.RenderTransform = new ScaleTransform { ScaleX = 0.8, ScaleY = 0.8, CenterX = 26, CenterY = 26 };
        var sb = new Storyboard();
        var fi = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(220), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(fi, ExpandButton); Storyboard.SetTargetProperty(fi, "Opacity"); sb.Children.Add(fi);
        var sx = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(280), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(sx, ExpandButton); Storyboard.SetTargetProperty(sx, "(UIElement.RenderTransform).(ScaleTransform.ScaleX)"); sb.Children.Add(sx);
        var sy = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(280), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(sy, ExpandButton); Storyboard.SetTargetProperty(sy, "(UIElement.RenderTransform).(ScaleTransform.ScaleY)"); sb.Children.Add(sy);
        sb.Begin();
    }

    private void ExpandButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isToolbarCollapsed) return;
        bool isMd = _loadedFormat == "markdown";
        if (isMd)
        {
            if (_isMdPreview) { _isMdPreview = false; PostMessageAsync("showWrite"); UpdateMdViewSwitch(true); } 
            ExpandMdToolbar();
            return;
        }

        
        ExpandHtmlToolbar();
    }

    /// <summary>N4D-01: html counterpart of ExpandMdToolbar - expand the HTML toolbar capsule after a fold.</summary>
    private void ExpandHtmlToolbar()
    {
        if (!_isToolbarCollapsed) return;
        _isToolbarCollapsed = false;
        MeltToolbarBack(ToolbarContainer, ToolbarTranslate);
    }

    private void ExpandMdToolbar()
    {
        if (!_isToolbarCollapsed) return;
        _isToolbarCollapsed = false;
        MeltToolbarBack(MdToolbarContainer, MdToolbarTranslate);
    }

    private void ColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isColorPickerOpen) { HideColorPickerPanel(); return; }
        HideAlignPickerPanel(); // only one picker may be open at a time
        ShowColorPickerPanel();
    }

    private void ShowColorPickerPanel()
    {
        _isColorPickerOpen = true;
        var t = ColorButton.TransformToVisual(ToolbarContainer);
        var p = t.TransformPoint(new Windows.Foundation.Point(20, 0));
        var width = ToolbarContainer.ActualWidth;
        ColorPickerTranslate.X = width > 0 ? p.X - width / 2 : p.X;
        ColorPickerScrim.Visibility = Visibility.Visible;
        ColorPickerPanel.Visibility = Visibility.Visible; ColorPickerPanel.Opacity = 0; ColorPickerTranslate.Y = -12;
        var sb = new Storyboard();
        var fi = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(230), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(fi, ColorPickerPanel); Storyboard.SetTargetProperty(fi, "Opacity"); sb.Children.Add(fi);
        var si = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(260), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(si, ColorPickerPanel); Storyboard.SetTargetProperty(si, "(UIElement.RenderTransform).(TranslateTransform.Y)"); sb.Children.Add(si);
        sb.Begin();
    }

    private void HideColorPickerPanel()
    {
        if (!_isColorPickerOpen) return;
        _isColorPickerOpen = false;
        ColorPickerScrim.Visibility = Visibility.Collapsed;
        var sb = new Storyboard();
        var fo = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(180), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(fo, ColorPickerPanel); Storyboard.SetTargetProperty(fo, "Opacity"); sb.Children.Add(fo);
        var so = new DoubleAnimation { To = -12, Duration = TimeSpan.FromMilliseconds(200), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(so, ColorPickerPanel); Storyboard.SetTargetProperty(so, "(UIElement.RenderTransform).(TranslateTransform.Y)"); sb.Children.Add(so);
        sb.Completed += (_, _) => { ColorPickerPanel.Visibility = Visibility.Collapsed; }; sb.Begin();
    }

    private void ColorPickerScrim_Tapped(object sender, TappedRoutedEventArgs e)
    {
        HideColorPickerPanel();
    }

    private void ColorItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Tag is string tag && tag == "Clear")
            PostMessageAsync("execClear");
        else if (btn.Background is SolidColorBrush b)
            PostMessageAsync("execForeColor", new { color = $"#{b.Color.R:X2}{b.Color.G:X2}{b.Color.B:X2}" });
        HideColorPickerPanel();
    }

    // ================================================================

    // ================================================================

    private async void InsertButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        var files = await picker.PickMultipleFilesAsync();
        if (files == null || files.Count == 0) return;
        foreach (var f in files)
            await InsertImageAsBase64(f);
    }

    private async System.Threading.Tasks.Task InsertImageAsBase64(StorageFile file)
    {
        try
        {
            using var stream = await file.OpenReadAsync();
            using var ms = new System.IO.MemoryStream();
            await stream.AsStream().CopyToAsync(ms);
            var bytes = ms.ToArray();
            if (bytes.Length > 5 * 1024 * 1024) { App.ShowToast(App.GetString("DiaryEditor_ImageTooLarge")); return; }
            
            if (_currentImageBytes + bytes.Length > MaxDiaryImageBytes) { App.ShowToast(App.GetString("DiaryEditor_ImageTotalExceeded")); return; }
            _currentImageBytes += bytes.Length;
            var mime = file.FileType.ToLowerInvariant() switch { ".jpg" or ".jpeg" => "image/jpeg", _ => "image/png" };
            PostMessageAsync("insertImage", new { base64 = Convert.ToBase64String(bytes), filename = file.Name, mime });
        }
        catch { }
    }

    // ================================================================

    // ================================================================

    private void InitializeToolbarIcons()
    {
        var cv = XamlBindingHelper.ConvertValue;
        var fg = App.GetBrush("IconForegroundBrush");

        UndoPathIcon.Data = (Geometry)cv(typeof(Geometry), IconData.EditorUndo);
        RedoPathIcon.Data = (Geometry)cv(typeof(Geometry), IconData.EditorRedo);
        BoldPathIcon.Data = (Geometry)cv(typeof(Geometry), IconData.Bold);
        ItalicPathIcon.Data = (Geometry)cv(typeof(Geometry), IconData.Italic);
        UnderlinePathIcon.Data = MakeGeometryGroup(IconData.Underline);
        TextColorPathIcon.Data = (Geometry)cv(typeof(Geometry), IconData.TextColor);
        AlignPathIcon.Data = (Geometry)cv(typeof(Geometry), IconData.EditorAlign);
        AlignLeftPathIcon.Data = (Geometry)cv(typeof(Geometry), IconData.EditorAlignLeft);
        AlignCenterPathIcon.Data = (Geometry)cv(typeof(Geometry), IconData.EditorAlignCenter);
        AlignRightPathIcon.Data = (Geometry)cv(typeof(Geometry), IconData.EditorAlignRight);
        CodeBlockPathIcon.Data = (Geometry)cv(typeof(Geometry), IconData.MdCodeBlock);
        HorizontalRulePathIcon.Data = (Geometry)cv(typeof(Geometry), IconData.MdHr);
        InsertImagePathIcon.Data = (Geometry)cv(typeof(Geometry), IconData.InsertImage);
        CollapsePathIcon.Data = (Geometry)cv(typeof(Geometry), IconData.Collapse);

        InitializeRestoreIcon();

        
        MdWritePathIcon.Data = App.CreateGeometry(IconData.MdWrite);
        MdPreviewPathIcon.Data = App.CreateGeometry(IconData.MdPreview);
        MdUndoPathIcon.Data = App.CreateGeometry(IconData.EditorUndo);
        MdRedoPathIcon.Data = App.CreateGeometry(IconData.EditorRedo);
        MdBoldPathIcon.Data = App.CreateGeometry(IconData.Bold);
        MdItalicPathIcon.Data = App.CreateGeometry(IconData.Italic);
        MdStrikePathIcon.Data = App.CreateGeometry(IconData.EditorHorizontalRule);
        MdInlineCodePathIcon.Data = App.CreateGeometry(IconData.MdInlineCode);
        MdHeadingPathIcon.Data = App.CreateGeometry(IconData.MdHeading);
        MdBulletPathIcon.Data = App.CreateGeometry(IconData.MdBulletList);
        MdOrderedPathIcon.Data = App.CreateGeometry(IconData.MdOrderedList);
        MdQuotePathIcon.Data = App.CreateGeometry(IconData.MdQuote);
        MdLinkPathIcon.Data = App.CreateGeometry(IconData.MdLink);
        MdImagePathIcon.Data = App.CreateGeometry(IconData.InsertImage);
        MdCodeBlockPathIcon.Data = App.CreateGeometry(IconData.MdCodeBlock);
        MdHrPathIcon.Data = App.CreateGeometry(IconData.MdHr);
        MdTablePathIcon.Data = App.CreateGeometry(IconData.MdTable);
        MdClearFormatPathIcon.Data = App.CreateGeometry(IconData.MdClearFormat);
        MdHeading1PathIcon.Data = App.CreateGeometry(IconData.MdHeading1);
        MdHeading2PathIcon.Data = App.CreateGeometry(IconData.MdHeading2);
        MdHeading3PathIcon.Data = App.CreateGeometry(IconData.MdHeading3);
        MdHeading4PathIcon.Data = App.CreateGeometry(IconData.MdHeading4);
        MdCollapsePathIcon.Data = App.CreateGeometry(IconData.Collapse);
    }

    private void InitializeRestoreIcon()
    {
        // The original Restore multi-path icon rendered oddly in the 28x28 swatch; reuse the
        // redo glyph (single path, already verified) for the "clear format" reset button.
        var cv = XamlBindingHelper.ConvertValue;
        RestorePathIcon.Data = (Geometry)cv(typeof(Geometry), IconData.EditorRedo);
    }

    // ================================================================
    
    // ================================================================

    private void MdUndoButton_Click(object sender, RoutedEventArgs e) => PostMessageAsync("mdUndo");
    private void MdRedoButton_Click(object sender, RoutedEventArgs e) => PostMessageAsync("mdRedo");
    private void MdBoldButton_Click(object sender, RoutedEventArgs e) => PostMessageAsync("mdBold");
    private void MdItalicButton_Click(object sender, RoutedEventArgs e) => PostMessageAsync("mdItalic");
    private void MdStrikeButton_Click(object sender, RoutedEventArgs e) => PostMessageAsync("mdStrike");
    private void MdInlineCodeButton_Click(object sender, RoutedEventArgs e) => PostMessageAsync("mdInlineCode");
    private void MdBulletButton_Click(object sender, RoutedEventArgs e) => PostMessageAsync("mdBullet");
    private void MdOrderedButton_Click(object sender, RoutedEventArgs e) => PostMessageAsync("mdOrdered");
    private void MdQuoteButton_Click(object sender, RoutedEventArgs e) => PostMessageAsync("mdQuote");
    private void MdLinkButton_Click(object sender, RoutedEventArgs e) => PostMessageAsync("mdLink");
    private void MdImageButton_Click(object sender, RoutedEventArgs e) => PostMessageAsync("mdImage");
    private void MdHrButton_Click(object sender, RoutedEventArgs e) => PostMessageAsync("mdHr");
    private void MdTableButton_Click(object sender, RoutedEventArgs e) => PostMessageAsync("mdTable");
    private void MdClearFormatButton_Click(object sender, RoutedEventArgs e) => PostMessageAsync("mdClearFormat");

    private void MdCollapseButton_Click(object sender, RoutedEventArgs e) => MeltToolbarAway(MdToolbarContainer, MdToolbarTranslate);

    private void CollapseMdToolbar()
    {
        if (_isToolbarCollapsed) return;
        HideMdHeadingPickerPanel();
        _isToolbarCollapsed = true;
        var sb = new Storyboard();
        var f = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(280), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(f, MdToolbarContainer); Storyboard.SetTargetProperty(f, "Opacity"); sb.Children.Add(f);
        var s = new DoubleAnimation { To = 24, Duration = TimeSpan.FromMilliseconds(320), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(s, MdToolbarTranslate); Storyboard.SetTargetProperty(s, "TranslateY"); sb.Children.Add(s);
        sb.Completed += (_, _) => { MdToolbarContainer.Visibility = Visibility.Collapsed; ShowExpandButton(); }; sb.Begin();
    }

    private void MdHeadingButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isMdHeadingPickerOpen) { HideMdHeadingPickerPanel(); return; }
        ShowMdHeadingPickerPanel();
    }

    private void MdHeadingItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string lvl && int.TryParse(lvl, out int level))
            PostMessageAsync("mdHeading", new { level });
        HideMdHeadingPickerPanel();
    }

    private void ShowMdHeadingPickerPanel()
    {
        _isMdHeadingPickerOpen = true;
        var t = MdHeadingButton.TransformToVisual(MdToolbarContainer);
        var p = t.TransformPoint(new Windows.Foundation.Point(18, 0));
        var width = MdToolbarContainer.ActualWidth;
        MdHeadingPickerTranslate.X = width > 0 ? p.X - width / 2 : p.X;
        MdHeadingPickerScrim.Visibility = Visibility.Visible;
        MdHeadingPickerPanel.Visibility = Visibility.Visible; MdHeadingPickerPanel.Opacity = 0; MdHeadingPickerTranslate.Y = -12;
        var sb = new Storyboard();
        var fi = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(230), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(fi, MdHeadingPickerPanel); Storyboard.SetTargetProperty(fi, "Opacity"); sb.Children.Add(fi);
        var si = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(260), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(si, MdHeadingPickerPanel); Storyboard.SetTargetProperty(si, "(UIElement.RenderTransform).(TranslateTransform.Y)"); sb.Children.Add(si);
        sb.Begin();
    }

    private void HideMdHeadingPickerPanel()
    {
        if (!_isMdHeadingPickerOpen) return;
        _isMdHeadingPickerOpen = false;
        MdHeadingPickerScrim.Visibility = Visibility.Collapsed;
        var sb = new Storyboard();
        var fo = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(180), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(fo, MdHeadingPickerPanel); Storyboard.SetTargetProperty(fo, "Opacity"); sb.Children.Add(fo);
        var so = new DoubleAnimation { To = -12, Duration = TimeSpan.FromMilliseconds(200), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(so, MdHeadingPickerPanel); Storyboard.SetTargetProperty(so, "(UIElement.RenderTransform).(TranslateTransform.Y)"); sb.Children.Add(so);
        sb.Completed += (_, _) => { MdHeadingPickerPanel.Visibility = Visibility.Collapsed; }; sb.Begin();
    }

    private void MdHeadingPickerScrim_Tapped(object sender, TappedRoutedEventArgs e)
    {
        HideMdHeadingPickerPanel();
    }

    private void MdCodeBlockButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout { MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"] };
        var langs = new[] { "csharp", "json", "markdown", "javascript", "python", "html", "css", "bash", "text" };
        foreach (var lang in langs)
        {
            string l = lang;
            var item = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = lang };
            item.Click += (_, _) => PostMessageAsync("mdCodeBlock", new { lang = l });
            menu.Items.Add(item);
        }
        menu.ShowAt(MdCodeBlockButton, new Windows.Foundation.Point(0, MdCodeBlockButton.ActualHeight + 4));
    }

    private void MdWriteButton_Click(object sender, RoutedEventArgs e) { _isMdPreview = false; PostMessageAsync("showWrite"); UpdateMdViewSwitch(true); if (_isToolbarCollapsed) ExpandMdToolbar(); }
    private void MdPreviewButton_Click(object sender, RoutedEventArgs e) { _isMdPreview = true; PostMessageAsync("showPreview"); UpdateMdViewSwitch(false); if (!_isToolbarCollapsed) CollapseMdToolbar(); }

    private void UpdateMdViewSwitch(bool write)
    {
        var sel = new SolidColorBrush(Color.FromArgb(0xFF, 0x72, 0x76, 0xFF));
        var trans = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        var white = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
        var icon = App.GetBrush("IconForegroundBrush");
        MdWriteButton.Background = write ? sel : trans;
        MdWritePathIcon.Fill = write ? white : icon;
        MdPreviewButton.Background = write ? trans : sel;
        MdPreviewPathIcon.Fill = write ? icon : white;
    }

    private void UpdateToolbarForFormat()
    {
        bool isMd = _loadedFormat == "markdown";
        ToolbarContainer.Visibility = isMd ? Visibility.Collapsed : Visibility.Visible;
        MdToolbarContainer.Visibility = isMd ? Visibility.Visible : Visibility.Collapsed;
        MdViewSwitch.Visibility = isMd ? Visibility.Visible : Visibility.Collapsed;
        ExpandButton.Visibility = Visibility.Collapsed; 
    }

    private GeometryGroup MakeGeometryGroup(string[] paths)
    {
        var cv = XamlBindingHelper.ConvertValue;
        var group = new GeometryGroup();
        foreach (var p in paths)
            group.Children.Add((Geometry)cv(typeof(Geometry), p));
        return group;
    }

    private void InitializeExpandIcon()
    {

        ExpandButton.ApplyTemplate();
        var queue = new System.Collections.Generic.Queue<DependencyObject>();
        int count = VisualTreeHelper.GetChildrenCount(ExpandButton);
        for (int i = 0; i < count; i++)
            queue.Enqueue(VisualTreeHelper.GetChild(ExpandButton, i));
        while (queue.Count > 0)
        {
            var child = queue.Dequeue();
            if (child is Microsoft.UI.Xaml.Shapes.Path path && path.Name == "ToolbarExpandPathIcon")
            {
                path.Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), IconData.ToolbarExpand);
                return;
            }
            int c = VisualTreeHelper.GetChildrenCount(child);
            for (int i = 0; i < c; i++)
                queue.Enqueue(VisualTreeHelper.GetChild(child, i));
        }
    }
}
