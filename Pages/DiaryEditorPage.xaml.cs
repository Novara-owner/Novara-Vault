using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Input;
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
    private string? _navigatingFormat;
    private bool _isBold, _isItalic, _isUnderline;
    private bool _webViewReady;



    private bool _editorInitInFlight;


    private Microsoft.Web.WebView2.Core.CoreWebView2Environment? _editorEnv;
    private string _loadedFormat = "html";
    private string? _initialTitle;
    private string? _initialContent;
    private bool _isMdHeadingPickerOpen;

    internal enum MdViewMode { Write, Split, Preview }

    private MdViewMode _mdView = MdViewMode.Write;
    private static readonly JsonSerializerOptions JsonCaseInsensitive = new() { PropertyNameCaseInsensitive = true };


    private const long MaxDiaryImageBytes = 20L * 1024 * 1024;
    private long _currentImageBytes;








private const string EditorHtmlTemplate = @"<!DOCTYPE html>
<html><head><meta charset='utf-8'><style>
*{{margin:0;padding:0;box-sizing:border-box}}
html,body{{background:{0};font-family:'Segoe UI',sans-serif;color:{1}}}
body{{overflow-y:auto;color:{1};padding-bottom:80px}}
body::-webkit-scrollbar{{width:4px;height:4px}}body::-webkit-scrollbar-track{{background:transparent}}body::-webkit-scrollbar-thumb{{background:{5};border-radius:2px}}body::-webkit-scrollbar-thumb:hover{{background:{11}}}
::selection{{background:{4};color:inherit}}
#title{{font-size:17px;font-weight:normal;letter-spacing:0.18em;line-height:26px;outline:none;padding:4px 0 8px 0;word-wrap:break-word;color:{1}}}
#title b,#title strong,#title span[style*=bold]{{font-weight:700!important}}
#title:empty::before{{content:'{6}';color:{2};font-weight:normal;letter-spacing:0.18em}}
#body.ph .ProseMirror p:first-child::before{{content:'{7}';color:{2};float:left;height:0;pointer-events:none;}}
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
#body .ProseMirror a{{color:{12};text-decoration:underline;cursor:pointer}}
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
  onUpdate:function(){{if(window.__mdLoading!==true)window.chrome.webview.postMessage(JSON.stringify({{action:'userEdited'}}))}},
  onSelectionUpdate:function(){{notifyFormatState()}}
}});
function notifyFormatState(){{window.chrome.webview.postMessage(JSON.stringify({{action:'formatState',bold:editor.isActive('bold'),italic:editor.isActive('italic'),underline:editor.isActive('underline')}}))}}
function updPh(){{var b=document.getElementById('body');if(editor.isEmpty)b.classList.add('ph');else b.classList.remove('ph')}}
editor.on('update',function(){{updPh()}});editor.on('create',function(){{updPh()}});
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
function setAll(ht,hb){{tel.textContent=ht||'';window.__mdLoading=true;editor.commands.setContent(hb||'');window.__mdLoading=false;updateTitleSpacing();updPh()}}
function updateTitleSpacing(){{var t=tel.textContent||'';var sp='0.18em';for(var i=0;i<t.length;i++){{var c=t.charCodeAt(i);if((c>=65&&c<=90)||(c>=97&&c<=122)||(c>=0xAC00&&c<=0xD7AF)||(c>=0x1100&&c<=0x11FF)||(c>=0x3040&&c<=0x30FF)){{sp='0';break}}}}tel.style.letterSpacing=sp}}
tel.addEventListener('input',function(){{updateTitleSpacing();window.chrome.webview.postMessage(JSON.stringify({{action:'userEdited'}}))}});
function getTitle(){{return tel.innerHTML}}function getBody(){{return editor.getHTML()}}
bel.addEventListener('click',function(e){{var a=e.target&&e.target.closest?e.target.closest('a'):null;if(a){{e.preventDefault();var href=a.getAttribute('href');if(href)window.chrome.webview.postMessage(JSON.stringify({{action:'openLink',url:href}}))}}}});
bel.addEventListener('input',function(){{window.chrome.webview.postMessage(JSON.stringify({{action:'userEdited'}}))}});
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
tel.addEventListener('keydown',function(e){{if(e.key==='Enter'&&!e.isComposing&&e.keyCode!==229){{e.preventDefault()}}}});
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
        string scrollbar = "rgba(114,118,255,0.45)";
        string scrollbarHover = "rgba(114,118,255,0.7)";
        string menuBg = isLight ? "#FFFFFF" : "#2A2A2A";
        string link = "#8C93FF";

        if (PaperTheme.TryGetEditorColors(out var pBg, out var pText, out var pPlaceholder, out var pSep, out var pSelection, out var pScrollbar, out var pScrollbarHover, out var pMenuBg, out var pLink))
        {
            bg = pBg; text = pText; placeholder = pPlaceholder; sep = pSep; selection = pSelection; scrollbar = pScrollbar; scrollbarHover = pScrollbarHover; menuBg = pMenuBg; link = pLink;
        }


        string bundle;
        try { bundle = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "tiptap.bundle.js")); }
        catch (Exception ex) { Services.CrashLogger.LogNote("EditorBundleMissing", "tiptap: " + ex.Message); bundle = "window.NovaraTiptap={};"; }
        return string.Format(EditorHtmlTemplate, bg, text, placeholder, sep, selection, scrollbar,
            App.GetString("DiaryEditor_TitlePlaceholder"), App.GetString("DiaryEditor_BodyPlaceholder"),
            bundle, App.GetString("Menu_Unlink"), menuBg, scrollbarHover, link);
    }
















    private const string MarkdownHtmlTemplate = @"<!DOCTYPE html>
<html><head><meta charset='utf-8'><style>
*{{margin:0;padding:0;box-sizing:border-box}}
html,body{{background:{0};font-family:'Segoe UI',sans-serif;color:{1}}}
body{{overflow-y:auto;color:{1};padding-bottom:80px}}
body::-webkit-scrollbar{{width:4px;height:4px}}body::-webkit-scrollbar-track{{background:transparent}}body::-webkit-scrollbar-thumb{{background:{4};border-radius:2px}}body::-webkit-scrollbar-thumb:hover{{background:{8}}}
#title{{font-size:17px;font-weight:normal;letter-spacing:0.18em;line-height:26px;outline:none;padding:4px 0 8px 0;word-wrap:break-word;color:{1}}}
#title:empty::before{{content:'{2}';color:{3};font-weight:normal;letter-spacing:0.18em}}
#md-editor::placeholder{{color:{3}}}
.sep{{height:1px;background:{5};margin:0 0 16px 0;opacity:0.45}}
#md-editor{{width:100%;min-height:calc(100vh - 150px);background:transparent;border:none;outline:none;resize:none;overflow-y:hidden;color:{1};font-family:'Segoe UI',sans-serif;font-size:14px;line-height:22px;padding:0}}
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
#md-preview a{{color:{9};text-decoration:underline}}
#md-preview img{{max-width:100%;height:auto}}
::selection{{background:{10};color:inherit}}
body.split{{display:grid;grid-template-columns:var(--split,50%) 6px 1fr;grid-template-rows:minmax(0,1fr);height:100vh;overflow:hidden;margin:0;padding-bottom:0}}
body.split .sep{{display:none}}
body.split #md-split-col{{display:block;grid-column:1;grid-row:1;min-width:0;min-height:0;overflow-y:auto;scrollbar-width:none;padding-bottom:80px}}
body.split #md-split-col::-webkit-scrollbar{{display:none!important;width:0;height:0}}
body.split #md-editor{{display:block;height:auto;min-height:0;min-width:0;overflow:hidden}}
body.split #md-divider{{display:block;grid-column:2;grid-row:1;width:6px;margin:0;cursor:col-resize;background:transparent;border-left:1px solid {5};}}
body.split #md-divider:hover,body.split #md-divider.dragging{{background:{4};border-left-color:{4}}}
body.split #md-preview{{grid-column:3;grid-row:1;display:block;min-height:0;min-width:0;overflow-x:hidden;overflow-y:auto;padding-left:16px;padding-bottom:80px}}
body.split #md-preview::-webkit-scrollbar{{width:4px}}
body.split #md-preview::-webkit-scrollbar-thumb{{background:{4};border-radius:2px}}
#md-preview table{{max-width:100%}}
#md-preview{{scroll-behavior:smooth}}
.mdb{{min-width:0}}
#md-mirror{{position:absolute;top:0;left:-99999px;visibility:hidden;white-space:pre-wrap;word-wrap:break-word;font-family:'Segoe UI',sans-serif;font-size:14px;line-height:22px;padding:0;color:{1}}}
</style></head><body>
<div id='md-split-col'>
<div id='title' contenteditable='true' spellcheck='false'></div>
<div class='sep'></div>
<textarea id='md-editor' spellcheck='false' placeholder='{7}'></textarea>
</div>
<div id='md-divider'></div>
<div id='md-preview'></div>
<div id='md-mirror' aria-hidden='true'></div>
<script>{6}</script>
<script>
var tel=document.getElementById('title');
var ta=document.getElementById('md-editor');
var pv=document.getElementById('md-preview');
var col=document.getElementById('md-split-col');
function titleH(){{return tel.offsetHeight}}
function setMd(t,b){{tel.textContent=t||'';ta.value=b||'';updateTitleSpacing();autoResize();renderPreview();rebuildMirror()}}
function autoResize(){{if(document.body.classList.contains('split')){{var prev=col.scrollTop;ta.style.height='auto';ta.style.height=ta.scrollHeight+'px';if(col.scrollTop!==prev)col.scrollTop=prev;return}}var doc=document.documentElement;var prev=doc.scrollTop;ta.style.height='auto';ta.style.height=ta.scrollHeight+'px';if(doc.scrollTop!==prev)doc.scrollTop=prev}}
function syncAfterEdit(){{autoResize();rebuildMirror();renderPreview();if(document.body.classList.contains('split')){{clearTimeout(window._mdSplitT);window._mdSplitT=setTimeout(function(){{alignPreview(cursorLine(),true)}},150)}}}}
function renderPreview(){{
var bs=splitBlocks(ta.value);var h='';for(var i=0;i<bs.length;i++){{h+='<div class=\'mdb\' data-sl=\''+(bs[i].s+1)+'\'>'+window.NovaraMd.render(bs[i].t)+'</div>'}}pv.innerHTML=h||'<div class=\'mdb\' data-sl=\'1\'></div>'}}
var mr=document.getElementById('md-mirror');var _lineTops=[0];
function rebuildMirror(){{
mr.style.width=ta.clientWidth+'px';while(mr.firstChild)mr.removeChild(mr.firstChild);var ls=ta.value.split('\n');_lineTops=new Array(ls.length);for(var i=0;i<ls.length;i++){{var d=document.createElement('div');d.textContent=ls[i]===''?'\u00a0':ls[i];mr.appendChild(d)}}var kids=mr.children;for(var i=0;i<kids.length;i++)_lineTops[i]=kids[i].offsetTop}}
function lineToY(L){{return L>=1&&L<=_lineTops.length?_lineTops[L-1]:0}}
function yToLine(y){{var lo=0,hi=_lineTops.length-1;while(lo<hi){{var mid=(lo+hi+1)>>1;if(_lineTops[mid]<=y)lo=mid;else hi=mid-1}}return lo+1}}
function cursorLine(){{return ta.value.slice(0,ta.selectionStart).split('\n').length}}
function splitBlocks(text){{var ls=text.split('\n');var bs=[];var cur=-2;for(var i=0;i<ls.length;i++){{if(/^\s*$/.test(ls[i])){{cur=-2;continue}}if(cur===-2){{cur=i;bs.push({{s:i,t:ls[i]}})}}else{{bs[bs.length-1].t+='\n'+ls[i]}}}}return bs}}
function alignPreview(L,smooth){{
var el=null;var bs=pv.querySelectorAll('.mdb');for(var i=bs.length-1;i>=0;i--){{if(parseInt(bs[i].getAttribute('data-sl'))<=L){{el=bs[i];break}}}}if(!el)return;var sl=parseInt(el.getAttribute('data-sl'));var yPv=el.offsetTop+(L-sl)*22;
var yCode=Math.max(0,titleH()+lineToY(L)-col.scrollTop);var target=yPv-yCode;var rel=target-pv.scrollTop;var h=pv.clientHeight;if(smooth&&rel>-h*0.25&&rel<h*0.75)return;
if(smooth)pv.scrollTo({{top:target,behavior:'smooth'}});else pv.scrollTop=target}}
function getTitle(){{return tel.innerHTML}}
function getMd(){{return ta.value}}
function fracOf(el){{var doc=document.documentElement;return (doc.scrollTop-el.offsetTop+8)/Math.max(1,el.scrollHeight)}}
function showWrite(){{var fromSplit=document.body.classList.contains('split');var L=0;if(fromSplit){{L=yToLine(Math.max(0,col.scrollTop-titleH()))+1;document.body.classList.remove('split');dvEnd();tel.setAttribute('contenteditable','true');rebuildMirror()}}var doc=document.documentElement;var vis=pv.scrollHeight>1;var f=vis?(doc.scrollTop-pv.offsetTop+8)/Math.max(1,pv.scrollHeight):0;ta.style.display='block';pv.style.display='none';autoResize();if(fromSplit){{doc.scrollTop=Math.max(0,ta.offsetTop+lineToY(L))}}else if(vis){{doc.scrollTop=Math.max(0,ta.offsetTop+f*ta.scrollHeight-8)}}}}
function showPreview(){{var fromSplit=document.body.classList.contains('split');var L=0;if(fromSplit){{L=yToLine(Math.max(0,col.scrollTop-titleH()))+1}}document.body.classList.remove('split');dvEnd();tel.setAttribute('contenteditable','false');var doc=document.documentElement;var f=(doc.scrollTop-ta.offsetTop+8)/Math.max(1,ta.scrollHeight);renderPreview();rebuildMirror();ta.style.display='none';pv.style.display='block';if(fromSplit){{doc.scrollTop=Math.max(0,ta.offsetTop+lineToY(L))}}else{{doc.scrollTop=Math.max(0,pv.offsetTop+f*pv.scrollHeight-8)}}var anchored=doc.scrollTop;var imgs=pv.querySelectorAll('img');for(var i=0;i<imgs.length;i++){{imgs[i].addEventListener('load',function(){{if(pv.style.display!=='none'&&doc.scrollTop===anchored){{doc.scrollTop=Math.max(0,pv.offsetTop+f*pv.scrollHeight-8);anchored=doc.scrollTop}}}})}}}}
function showSplit(){{var doc=document.documentElement;var docY=doc.scrollTop;var intoTa=Math.max(0,docY-ta.offsetTop);var L=yToLine(intoTa)+1;
document.body.classList.add('split');dvEnd();ta.style.display='block';pv.style.display='block';autoResize();document.body.style.setProperty('--split','50%');rebuildMirror();renderPreview();var _reflow=col.clientHeight;col.scrollTop=titleH()+lineToY(L);alignPreview(L,false);requestAnimationFrame(function(){{col.scrollTop=titleH()+lineToY(L);alignPreview(L,false);}});}}
var dv=document.getElementById('md-divider');var dragDv=false;
function dvEnd(){{dragDv=false;dv.classList.remove('dragging')}}
dv.addEventListener('pointerdown',function(e){{dragDv=true;dv.classList.add('dragging');dv.setPointerCapture(e.pointerId);e.preventDefault()}});
dv.addEventListener('pointermove',function(e){{if(!dragDv)return;var W=document.documentElement.clientWidth;var pct=e.clientX/W*100;if(pct>=100){{window.chrome.webview.postMessage(JSON.stringify({{action:'exitSplit',mode:'write'}}));return}}if(pct<=0){{window.chrome.webview.postMessage(JSON.stringify({{action:'exitSplit',mode:'preview'}}));return}}document.body.style.setProperty('--split',pct+'%')}});
dv.addEventListener('pointerup',function(){{dvEnd()}});dv.addEventListener('pointercancel',function(){{dvEnd()}});
function wrapSel(before,after,ph){{var s=ta.selectionStart,e=ta.selectionEnd;var sel=ta.value.substring(s,e)||ph;ta.setRangeText(before+sel+after,s,e,'end');ta.setSelectionRange(s+before.length,s+before.length+sel.length);ta.focus()}}
function prefixLines(prefix){{var s=ta.selectionStart;var val=ta.value;var ls=val.lastIndexOf('\n',s-1)+1;ta.setRangeText(prefix,ls,ls,'end');ta.focus()}}
function insertBlock(text){{var s=ta.selectionStart,e=ta.selectionEnd;ta.setRangeText(text,s,e,'end');ta.focus()}}
function mdBold(){{wrapSel('**','**','bold');syncAfterEdit();}}
function mdItalic(){{wrapSel('*','*','italic');syncAfterEdit();}}
function mdStrike(){{wrapSel('~~','~~','text');syncAfterEdit();}}
function mdInlineCode(){{wrapSel('`','`','code');syncAfterEdit();}}
function mdHeading(n){{prefixLines('#'.repeat(n)+' ');syncAfterEdit();}}
function mdBullet(){{prefixLines('- ');syncAfterEdit();}}
function mdOrdered(){{prefixLines('1. ');syncAfterEdit();}}
function mdQuote(){{prefixLines('> ');syncAfterEdit();}}
function mdLink(){{var s=ta.selectionStart,e=ta.selectionEnd;var sel=ta.value.substring(s,e);var looksUrl=sel.length>0&&!/\s/.test(sel)&&/^[a-zA-Z0-9-]/.test(sel)&&/\.[a-zA-Z0-9-]{{2,}}/.test(sel);var href=looksUrl?(sel.indexOf('://')>=0?sel:'https://'+sel):'url';var text=sel.length>0?sel:'链接';ta.setRangeText('['+text+']('+href+')',s,e,'end');if(!looksUrl){{var hs=s+text.length+3;ta.setSelectionRange(hs,hs+3);}}ta.focus();autoResize();syncAfterEdit();}}
function mdImage(){{wrapSel('![','](url)','alt');syncAfterEdit();}}
function mdCodeBlock(lang){{var s=ta.selectionStart,e=ta.selectionEnd;var sel=ta.value.substring(s,e);ta.setRangeText('```'+(lang||'')+'\n'+sel+'\n```',s,e,'end');ta.focus();syncAfterEdit();}}
function mdHr(){{insertBlock('\n\n---\n\n');syncAfterEdit();}}
function mdTable(){{insertBlock('|  |  |  |\n|---|---|---|\n|  |  |  |\n|  |  |  |\n');syncAfterEdit();}}
function mdClearFormat(){{var s=ta.selectionStart,e=ta.selectionEnd;var sel=ta.value.substring(s,e);ta.setRangeText(sel.replace(/[*_~`>#]/g,''),s,e,'end');ta.focus();syncAfterEdit();}}
function mdUndo(){{ta.focus();document.execCommand('undo');syncAfterEdit();}}
function mdRedo(){{ta.focus();document.execCommand('redo');syncAfterEdit();}}
function updateTitleSpacing(){{var t=tel.textContent||'';var sp='0.18em';for(var i=0;i<t.length;i++){{var c=t.charCodeAt(i);if((c>=65&&c<=90)||(c>=97&&c<=122)||(c>=0xAC00&&c<=0xD7AF)||(c>=0x1100&&c<=0x11FF)||(c>=0x3040&&c<=0x30FF)){{sp='0';break}}}}tel.style.letterSpacing=sp}}
tel.addEventListener('input',function(){{updateTitleSpacing();autoResize();window.chrome.webview.postMessage(JSON.stringify({{action:'userEdited'}}))}});
ta.addEventListener('input',function(){{autoResize();rebuildMirror();if(document.body.classList.contains('split')){{clearTimeout(window._mdSplitT);window._mdSplitT=setTimeout(function(){{renderPreview();alignPreview(cursorLine(),true)}},150)}}}});
tel.addEventListener('keydown',function(e){{var t=tel.textContent.replace(/\s/g,'');if(t.length>=120&&e.key.length===1&&!e.ctrlKey&&!e.metaKey&&!e.isComposing&&e.keyCode!==229){{e.preventDefault()}}}});
tel.addEventListener('drop',function(e){{if(e.target===tel||tel.contains(e.target))e.preventDefault()}});
tel.addEventListener('keydown',function(e){{if(e.key==='Enter'&&!e.isComposing&&e.keyCode!==229){{e.preventDefault()}}}});
tel.addEventListener('paste',function(e){{e.preventDefault();var txt=(e.clipboardData||window.clipboardData).getData('text/plain')||'';var sel=window.getSelection();if(sel&&sel.rangeCount&&tel.contains(sel.anchorNode)){{try{{sel.deleteFromDocument()}}catch(err){{}}}}var t=tel.textContent.replace(/\s/g,'');var rem=120-t.length;if(rem<=0)return;var out='',ns=0;for(var i=0;i<txt.length;i++){{var ch=txt.charAt(i);out+=ch;if(!/\s/.test(ch)){{ns++;if(ns>=rem)break}}}}document.execCommand('insertText',false,out)}});
window.chrome.webview.addEventListener('message',function(e){{try{{var m=JSON.parse(e.data);switch(m.action){{case'setMd':setMd(m.title||'',m.body||'');break;case'showWrite':showWrite();break;case'showPreview':showPreview();break;case'showSplit':showSplit();break;case'mdBold':mdBold();break;case'mdItalic':mdItalic();break;case'mdStrike':mdStrike();break;case'mdInlineCode':mdInlineCode();break;case'mdHeading':mdHeading(m.level||1);break;case'mdBullet':mdBullet();break;case'mdOrdered':mdOrdered();break;case'mdQuote':mdQuote();break;case'mdLink':mdLink();break;case'mdImage':mdImage();break;case'mdCodeBlock':mdCodeBlock(m.lang||'');break;case'mdHr':mdHr();break;case'mdTable':mdTable();break;case'mdClearFormat':mdClearFormat();break;case'mdUndo':mdUndo();break;case'mdRedo':mdRedo();break;}}}}catch(err){{}}}});
</script></body></html>";

    private string GetMarkdownHtml()
    {
        bool isLight = App.CurrentTheme == "浅色模式"
            || (App.MainWindow?.Content is FrameworkElement root && root.ActualTheme == ElementTheme.Light);
        string bg = isLight ? "#F2F2F2" : "#1E1E1E";
        string text = isLight ? "rgba(0,0,0,0.87)" : "rgba(255,255,255,0.87)";
        string placeholder = isLight ? "rgba(0,0,0,0.35)" : "rgba(255,255,255,0.4)";
        string sep = isLight ? "rgba(0,0,0,0.12)" : "rgba(255,255,255,0.2)";
        string scrollbar = "rgba(114,118,255,0.45)";
        string scrollbarHover = "rgba(114,118,255,0.7)";
        string link = "#8C93FF";

        if (PaperTheme.TryGetEditorColors(out var pBg, out var pText, out var pPlaceholder, out var pSep, out var pSelection, out var pScrollbar, out var pScrollbarHover, out var pMenuBg, out var pLink))
        {
            bg = pBg; text = pText; placeholder = pPlaceholder; sep = pSep; scrollbar = pScrollbar; scrollbarHover = pScrollbarHover; link = pLink;
        }
        string bundle;
        try { bundle = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "md.bundle.js")); }
        catch { bundle = "window.NovaraMd={render:function(t){return (t||'').replace(/</g,'&lt;')}};"; }

        string selection = isLight ? "rgba(0,0,0,0.15)" : "rgba(255,255,255,0.2)";
        if (PaperTheme.TryGetEditorColors(out var mBg, out var mText, out var mPh, out var mSep, out var mSel, out var mSb, out var mSbH, out var mMenu, out var mLink))
        { selection = mSel; }
        return string.Format(MarkdownHtmlTemplate, bg, text, App.GetString("DiaryEditor_DocumentTitlePlaceholder"), placeholder, scrollbar, sep, bundle,
            App.GetString("DiaryEditor_DocumentBodyPlaceholder"), scrollbarHover, link, selection);
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





    internal static bool IsUntitledStored(string title)
        => string.IsNullOrWhiteSpace(title)
           || title == "(无标题)"
           || title == App.GetString("DiaryEditor_Untitled");

    public DiaryEditorPage()
    {
        InitializeComponent();
        Novara.Services.DialogDepth.AttachContainer((Grid)Content, autoVeil: true);
        Loaded += Page_Loaded;
        Unloaded += Page_Unloaded;
        KeyDown += Page_KeyDown;
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

    private void OnNavigationStarting(Microsoft.Web.WebView2.Core.CoreWebView2 sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs e)
    {
        var uri = e.Uri;



        if (uri == "about:blank#blocked")
        {
            e.Cancel = true;
            return;
        }


        if (uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            e.Cancel = true;
            OpenExternalLink(uri.ToString());
        }
        else if (!uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) &&
                 !uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
        {




            e.Cancel = true;
        }
    }






    private void OnNewWindowRequested(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (!string.IsNullOrEmpty(e.Uri)) OpenExternalLink(e.Uri);
    }

    private void OnNavigationCompleted(Microsoft.UI.Xaml.Controls.WebView2 sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {



            Services.CrashLogger.LogNote("DiaryEditorNavigationFailed", $"日记编辑器导航失败: {e.WebErrorStatus}");
            _webViewReady = false;
            return;
        }
        _navigatingFormat = null;
        _webViewReady = true;
        SetDiaryContent(_currentDiary);
        _ = RebaselineContentAfterRenderAsync();
        UpdateToolbarForFormat();
        SetMdView(MdViewMode.Write);
    }





    private bool _userEditedSinceRender;







    private async System.Threading.Tasks.Task RebaselineContentAfterRenderAsync()
    {
        try
        {
            await System.Threading.Tasks.Task.Delay(500);
            if (!_webViewReady || _loadedFormat == "markdown") return;
            if (_userEditedSinceRender) return;
            var body = await GetJsStringAsync("getBody()");



            if (body != null) _initialContent = HtmlSanitizer.Sanitize(body);
        }
        catch { }
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
            else if (action == "exitSplit")
            {
                var mode = doc.RootElement.TryGetProperty("mode", out var mv) ? mv.GetString() : null;
                SetMdView(mode == "write" ? MdViewMode.Write : MdViewMode.Preview);
            }
            else if (action == "userEdited")
            {
                _userEditedSinceRender = true;
            }
        }
        catch { }
    }

    internal static void OpenExternalLink(string url)
    {
        var target = url.Trim();



        var colon = target.IndexOf(':');
        var slash = target.IndexOf('/');
        if (colon < 0 || (slash >= 0 && slash < colon)) target = "https://" + target;



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


        BackPathIcon.Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), IconData.Back);
        InitializeToolbarIcons();



        if (_webViewReady) return;
        EditorFailNotice.Visibility = Visibility.Collapsed;
        await RunEditorInitOrFailUiAsync("");
    }

    private async System.Threading.Tasks.Task RunEditorInitOrFailUiAsync(string attemptTag)
    {


        if (_editorInitInFlight || _webViewReady) return;
        _editorInitInFlight = true;
        string? err;
        try
        {
            err = await InitEditorWebViewAsync();
        }
        finally
        {
            _editorInitInFlight = false;
        }
        if (err == null) return;

        Services.CrashLogger.LogNote("EditorWebViewInitFailed", attemptTag + err);
        ShowEditorFailNotice(err);
    }






    private async System.Threading.Tasks.Task<Microsoft.Web.WebView2.Core.CoreWebView2Environment> GetOrCreateEditorEnvAsync(string udf)
    {
        if (_editorEnv == null)
            _editorEnv = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateWithOptionsAsync(null, udf, null);
        return _editorEnv;
    }


    private async System.Threading.Tasks.Task<string?> InitEditorWebViewAsync()
    {

        try { _ = Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString(); }
        catch (Exception ex) { return "runtime-missing: " + ex.Message; }




        var primary = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Novara.Services.CoreEnv.DataDirName, "Webview2");
        string udf = primary;
        if (!ProbeWritable(primary))
        {
            var alt = primary + "-alt";
            if (!ProbeWritable(alt)) return "udf-unwritable: " + primary;
            udf = alt;
            Services.CrashLogger.LogNote("EditorWebViewUdfFallback", "primary UDF not writable, using " + alt);
        }

        try
        {


            var env = await GetOrCreateEditorEnvAsync(udf);
            await ContentWebView.EnsureCoreWebView2Async(env);


            var bundlePath = System.IO.Path.Combine(AppContext.BaseDirectory, _loadedFormat == "markdown" ? "md.bundle.js" : "tiptap.bundle.js");
            if (!System.IO.File.Exists(bundlePath) || new System.IO.FileInfo(bundlePath).Length < 1024)
                return "bundle-missing: " + bundlePath;

            var cv = ContentWebView.CoreWebView2;
            if (cv == null) return "null-core";
            cv.Settings.IsScriptEnabled = true;
            cv.Settings.AreDefaultScriptDialogsEnabled = false;
            cv.Settings.IsWebMessageEnabled = true;

            cv.WebMessageReceived -= OnWebMessageReceived;
            cv.WebMessageReceived += OnWebMessageReceived;

            ContentWebView.NavigationCompleted -= OnNavigationCompleted;
            ContentWebView.NavigationCompleted += OnNavigationCompleted;


            cv.NavigationStarting -= OnNavigationStarting;
            cv.NavigationStarting += OnNavigationStarting;




            cv.NewWindowRequested -= OnNewWindowRequested;
            cv.NewWindowRequested += OnNewWindowRequested;

            ContentWebView.NavigateToString(_loadedFormat == "markdown" ? GetMarkdownHtml() : GetEditorHtml());
            _navigatingFormat = _loadedFormat;
            return null;
        }
        catch (Exception ex)
        {


            return ex.ToString();
        }
    }

    private static bool ProbeWritable(string dir)
    {
        try
        {
            System.IO.Directory.CreateDirectory(dir);
            var probe = System.IO.Path.Combine(dir, "novara-probe.tmp");
            System.IO.File.WriteAllText(probe, "1");
            System.IO.File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    private void ShowEditorFailNotice(string err)
    {
        bool runtimeMissing = err.StartsWith("runtime-missing", StringComparison.Ordinal);
        EditorFailTitle.Text = App.GetString("Editor_Fail_Title");
        EditorFailBody.Text = App.GetString(runtimeMissing ? "Editor_Fail_Runtime" : "Editor_Fail_Generic");
        EditorFailDetail.Text = err;
        EditorFailRetryButton.Content = App.GetString("Editor_Fail_Retry");
        EditorFailHelpLink.Content = App.GetString("Editor_Fail_Help");
        EditorFailHelpLink.Visibility = runtimeMissing ? Visibility.Visible : Visibility.Collapsed;
        EditorFailNotice.Visibility = Visibility.Visible;
    }

    private async void EditorFailRetry_Click(object sender, RoutedEventArgs e)
    {
        EditorFailNotice.Visibility = Visibility.Collapsed;
        if (_webViewReady) return;
        await RunEditorInitOrFailUiAsync("retry: ");
    }

    public void LoadDiary(DiaryEntry? diary, string? newFormat = null)
    {
        BackButton.IsEnabled = true;
        _currentDiary = diary;
        _currentImageBytes = diary != null ? EstimateImageBytes(diary.Content) : 0;
        var format = diary?.Format ?? newFormat ?? "html";
        if (diary != null)
        {
            _initialTitle = IsUntitledStored(diary.Title) ? "" : diary.Title;
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
            _navigatingFormat = format;
        }
        else if (_webViewReady)
        {
            SetDiaryContent(diary);
            if (format != "markdown") _ = RebaselineContentAfterRenderAsync();
        }
        else if (_navigatingFormat != null && _navigatingFormat != format)
        {




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
        _userEditedSinceRender = false;
        string title = IsUntitledStored(diary.Title) ? "" : diary.Title;
        if (_loadedFormat == "markdown")
            PostMessageAsync("setMd", new { title = title, body = diary.Content });
        else
            PostMessageAsync("setAll", new { title = title, body = HtmlSanitizer.Sanitize(diary.Content) });
    }



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
        BackButton.IsEnabled = true;
        ResetFormatStates();
    }



    public void Shutdown()
    {
        _webViewReady = false;
        _navigatingFormat = null;
        try { ContentWebView.Close(); } catch { }
        _currentDiary = null;
        _currentImageBytes = 0;
        _initialTitle = null;
        _initialContent = null;
    }





    private void PostMessageAsync(string action, object? data = null)
    {
        try
        {
            var cv = ContentWebView.CoreWebView2;
            if (cv == null || !_webViewReady) { System.Diagnostics.Debug.WriteLine($"PostMessage 丢弃（WebView2 未就绪）: {action}"); return; }





            if (action.StartsWith("exec", StringComparison.Ordinal) || action == "insertImage") _userEditedSinceRender = true;
            var msg = new Dictionary<string, object> { ["action"] = action };
            if (data != null)
                foreach (var p in data.GetType().GetProperties())
                    msg[p.Name] = p.GetValue(data)!;
            cv.PostWebMessageAsString(JsonSerializer.Serialize(msg));


            if (action.StartsWith("md", StringComparison.Ordinal))
            {
                try { ContentWebView.Focus(FocusState.Programmatic); } catch { }
                _ = RefocusMdTextareaAsync(cv);
            }
        }
        catch { }
    }

    private static async System.Threading.Tasks.Task RefocusMdTextareaAsync(Microsoft.Web.WebView2.Core.CoreWebView2 cv)
    {
        try { await cv.ExecuteScriptAsync("(function(){var t=document.querySelector('textarea');if(t){try{t.focus({preventScroll:true})}catch(e){t.focus()}}})()"); } catch { }
    }

    private async System.Threading.Tasks.Task<string?> GetJsStringAsync(string script)
    {
        try
        {
            if (ContentWebView.CoreWebView2 == null) return null;
            var json = await ContentWebView.CoreWebView2.ExecuteScriptAsync(script);



            return JsonSerializer.Deserialize<string>(json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"执行 JS 失败: {ex}");
            return null;
        }
    }





    private async void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (!BackButton.IsEnabled) return;

        BackButton.IsEnabled = false;
        bool wasNew = _currentDiary == null;
        bool saved;
        try { saved = await SaveCurrentDiaryAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
        catch { saved = false; }
        if (saved) App.ShowToast(App.GetString(wasNew ? "Common_Toast_Created" : "Common_Toast_Modified"));
        else if (_userEditedSinceRender) App.ShowToast(App.GetString("Editor_SaveFail_Toast"), ToastTone.Error);
        App.MainWindow?.NavigateBackFromEditor();
    }




    public bool HasUnsavedEdits => _userEditedSinceRender;

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


            bodyHasContent = cleanedBody.Contains("<img", StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrWhiteSpace(System.Text.RegularExpressions.Regex.Replace(cleanedBody, "<.*?>", "").Trim());

        if (_currentDiary == null && string.IsNullOrEmpty(plainTitle) && !bodyHasContent)
        {


            _userEditedSinceRender = false;
            return false;
        }


        if (_currentDiary != null && plainTitle == _initialTitle && cleanedBody == _initialContent)
        {

            _userEditedSinceRender = false;
            return false;
        }

        var entry = _currentDiary ?? new DiaryEntry();
        if (_currentDiary == null && isMd) entry.Format = "markdown";



        entry.Title = plainTitle;
        entry.Content = cleanedBody;

        if (_currentDiary == null)
        {
            entry.CreatedAt = DateTime.Now;
            entry.ModifiedAt = entry.CreatedAt;
            entry.WorkspaceId = App.CurrentWorkspaceId;
        }
        else
        {
            entry.ModifiedAt = DateTime.Now;
        }

        App.MainWindow?.UpsertDiary(entry);



        _currentDiary = entry;
        _initialTitle = plainTitle;
        _initialContent = cleanedBody;



        _userEditedSinceRender = false;
        return true;
    }










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
        HideColorPickerPanel();
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
        var fi = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(200), EasingFunction = Services.Motion.Decelerate() };
        Storyboard.SetTarget(fi, AlignPickerPanel); Storyboard.SetTargetProperty(fi, "Opacity"); sb.Children.Add(fi);
        var si = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(220), EasingFunction = Services.Motion.Decelerate() };
        Storyboard.SetTarget(si, AlignPickerPanel); Storyboard.SetTargetProperty(si, "(UIElement.RenderTransform).(TranslateTransform.Y)"); sb.Children.Add(si);
        sb.Begin();
    }

    private void HideAlignPickerPanel()
    {
        if (!_isAlignPickerOpen) return;
        _isAlignPickerOpen = false;
        AlignPickerScrim.Visibility = Visibility.Collapsed;
        var sb = new Storyboard();
        var fo = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(150), EasingFunction = Services.Motion.Accelerate() };
        Storyboard.SetTarget(fo, AlignPickerPanel); Storyboard.SetTargetProperty(fo, "Opacity"); sb.Children.Add(fo);
        var so = new DoubleAnimation { To = -10, Duration = TimeSpan.FromMilliseconds(170), EasingFunction = Services.Motion.Accelerate() };
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

        HideAlignPickerPanel();
        HideColorPickerPanel();
        HideMdHeadingPickerPanel();
        _isBold = _isItalic = _isUnderline = false;



        _mdView = MdViewMode.Write;


        if (_webViewReady)
        {
            if (_loadedFormat == "markdown") { SetMdView(MdViewMode.Write); ExpandMdToolbar(); }
            else ExpandHtmlToolbar();
            UpdateFormatButtonStates();
        }
        else _isToolbarCollapsed = false;
    }







        private void MeltToolbarAway(Grid container, Microsoft.UI.Xaml.Media.CompositeTransform tr)
        {
            if (_isToolbarCollapsed) return;
            HideAlignPickerPanel();
            HideColorPickerPanel();
            HideMdHeadingPickerPanel();
            _isToolbarCollapsed = true;
            ExpandButton.Visibility = Visibility.Collapsed;
            var sb = new Storyboard();
            var ty = new DoubleAnimation { To = 16, Duration = TimeSpan.FromMilliseconds(180), EasingFunction = Services.Motion.Accelerate() };
            Storyboard.SetTarget(ty, tr); Storyboard.SetTargetProperty(ty, "TranslateY"); sb.Children.Add(ty);
            var f = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(180), EasingFunction = Services.Motion.Accelerate() };
            Storyboard.SetTarget(f, container); Storyboard.SetTargetProperty(f, "Opacity"); sb.Children.Add(f);
            sb.Completed += (_, _) => { container.Visibility = Visibility.Collapsed; tr.TranslateX = 0; tr.TranslateY = 0; tr.ScaleX = 1; tr.ScaleY = 1; container.Opacity = 1; ShowExpandButton(); };
            sb.Begin();
        }

        private void MeltToolbarBack(Grid container, Microsoft.UI.Xaml.Media.CompositeTransform tr)
        {
            ExpandButton.Visibility = Visibility.Collapsed;
            tr.TranslateX = 0; tr.TranslateY = 16; tr.ScaleX = 1; tr.ScaleY = 1;
            container.Opacity = 0; container.Visibility = Visibility.Visible;
            var sb = new Storyboard();
            var ty = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(240), EasingFunction = Services.Motion.Decelerate() };
            Storyboard.SetTarget(ty, tr); Storyboard.SetTargetProperty(ty, "TranslateY"); sb.Children.Add(ty);
            var f = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(200), EasingFunction = Services.Motion.Decelerate() };
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
            if (_mdView != MdViewMode.Write) SetMdView(MdViewMode.Write);
            ExpandMdToolbar();
            return;
        }


        ExpandHtmlToolbar();
    }


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
        HideAlignPickerPanel();
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
        var fi = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(200), EasingFunction = Services.Motion.Decelerate() };
        Storyboard.SetTarget(fi, ColorPickerPanel); Storyboard.SetTargetProperty(fi, "Opacity"); sb.Children.Add(fi);
        var si = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(220), EasingFunction = Services.Motion.Decelerate() };
        Storyboard.SetTarget(si, ColorPickerPanel); Storyboard.SetTargetProperty(si, "(UIElement.RenderTransform).(TranslateTransform.Y)"); sb.Children.Add(si);
        sb.Begin();
    }

    private void HideColorPickerPanel()
    {
        if (!_isColorPickerOpen) return;
        _isColorPickerOpen = false;
        ColorPickerScrim.Visibility = Visibility.Collapsed;
        var sb = new Storyboard();
        var fo = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(150), EasingFunction = Services.Motion.Accelerate() };
        Storyboard.SetTarget(fo, ColorPickerPanel); Storyboard.SetTargetProperty(fo, "Opacity"); sb.Children.Add(fo);
        var so = new DoubleAnimation { To = -10, Duration = TimeSpan.FromMilliseconds(170), EasingFunction = Services.Motion.Accelerate() };
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





    private async void InsertButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var files = FilePicker.PickFiles("*.png;*.jpg;*.jpeg");
            if (files.Count == 0) return;
            foreach (var f in files)
                await InsertImageFromPath(f);
        }
        catch (Exception ex) { CrashLogger.LogNote("DiaryImagePicker", ex.Message); }
    }

    private async System.Threading.Tasks.Task InsertImageFromPath(string path)
    {
        try
        {
            var bytes = await System.IO.File.ReadAllBytesAsync(path);
            if (bytes.Length > 5 * 1024 * 1024) { App.ShowToast(App.GetString("DiaryEditor_ImageTooLarge"), ToastTone.Error); return; }




            long existing = await QueryEmbeddedImageBytesAsync();
            if (existing + bytes.Length > MaxDiaryImageBytes) { App.ShowToast(App.GetString("DiaryEditor_ImageTotalExceeded"), ToastTone.Error); return; }
            _currentImageBytes = existing + bytes.Length;
            var mime = System.IO.Path.GetExtension(path).ToLowerInvariant() switch { ".jpg" or ".jpeg" => "image/jpeg", _ => "image/png" };
            PostMessageAsync("insertImage", new { base64 = Convert.ToBase64String(bytes), filename = System.IO.Path.GetFileName(path), mime });
        }
        catch { }
    }




    private async System.Threading.Tasks.Task<long> QueryEmbeddedImageBytesAsync()
    {
        try
        {
            if (ContentWebView.CoreWebView2 == null || !_webViewReady || _loadedFormat != "html") return _currentImageBytes;
            const string js = "(()=>{let s=0;document.querySelectorAll('#body img').forEach(im=>{const src=im.getAttribute('src')||'';const m=/^data:[^;]*;base64,(.*)$/.exec(src);if(m){try{s+=(atob(m[1])||'').length}catch(e){}}});return String(s)})()";
            var r = await ContentWebView.CoreWebView2.ExecuteScriptAsync(js);
            return long.TryParse(r, out var v) ? v : _currentImageBytes;
        }
        catch { return _currentImageBytes; }
    }





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


        var cv = XamlBindingHelper.ConvertValue;
        RestorePathIcon.Data = (Geometry)cv(typeof(Geometry), IconData.EditorColorReset);
    }





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

        var f = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(Services.Motion.DlgOut), EasingFunction = Services.Motion.Accelerate() };
        Storyboard.SetTarget(f, MdToolbarContainer); Storyboard.SetTargetProperty(f, "Opacity"); sb.Children.Add(f);
        var s2 = new DoubleAnimation { To = 24, Duration = TimeSpan.FromMilliseconds(Services.Motion.DlgOut), EasingFunction = Services.Motion.Accelerate() };
        Storyboard.SetTarget(s2, MdToolbarTranslate); Storyboard.SetTargetProperty(s2, "TranslateY"); sb.Children.Add(s2);
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



    private void MdWriteButton_Click(object sender, RoutedEventArgs e)
    {
        switch (_mdView)
        {
            case MdViewMode.Write: return;
            case MdViewMode.Split: SetMdView(MdViewMode.Preview); break;
            case MdViewMode.Preview: SetMdView(MdViewMode.Split); break;
        }
    }

    private void MdPreviewButton_Click(object sender, RoutedEventArgs e)
    {
        switch (_mdView)
        {
            case MdViewMode.Preview: return;
            case MdViewMode.Split: SetMdView(MdViewMode.Write); break;
            case MdViewMode.Write: SetMdView(MdViewMode.Split); break;
        }
    }



    private void SetMdView(MdViewMode mode)
    {
        _mdView = mode;
        PostMessageAsync(mode switch
        {
            MdViewMode.Write => "showWrite",
            MdViewMode.Split => "showSplit",
            _ => "showPreview",
        });
        UpdateMdViewSwitch(mode);

        if (mode == MdViewMode.Preview) { if (!_isToolbarCollapsed) CollapseMdToolbar(); }
        else { if (_isToolbarCollapsed) ExpandMdToolbar(); }
    }

    private void UpdateMdViewSwitch(MdViewMode mode)
    {
        var sel = new SolidColorBrush(PaperTheme.BrandColor);
        var trans = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        var white = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
        var icon = App.GetBrush("IconForegroundBrush");

        MdWriteButton.Background = mode != MdViewMode.Preview ? sel : trans;
        MdWritePathIcon.Fill = mode != MdViewMode.Preview ? white : icon;
        MdPreviewButton.Background = mode != MdViewMode.Write ? sel : trans;
        MdPreviewPathIcon.Fill = mode != MdViewMode.Write ? white : icon;
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
