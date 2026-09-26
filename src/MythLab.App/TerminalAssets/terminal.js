'use strict';
const terminal = new Terminal({cursorBlink:true,fontFamily:'Cascadia Mono, Consolas, monospace',fontSize:14,scrollback:2000,screenReaderMode:true,theme:{background:'#111820',foreground:'#e4e9ef'}});
const fit = new FitAddon.FitAddon();
terminal.loadAddon(fit);
terminal.open(document.getElementById('terminal'));
const post = value => chrome.webview.postMessage(value);
terminal.parser.registerOscHandler(52, () => true); // Remote applications cannot write the Windows clipboard.
terminal.onData(data => post({type:'input',data}));
terminal.onResize(({cols,rows}) => post({type:'resize',cols,rows}));
window.requestCopy = () => post({type:'copy',data:terminal.getSelection().slice(0,32768)});
terminal.attachCustomKeyEventHandler(e => {
 if(e.type==='keydown' && e.ctrlKey && e.shiftKey && e.code==='KeyC'){requestCopy();return false;}
 if(e.ctrlKey && e.shiftKey && e.code==='KeyV'){if(e.type==='keydown')post({type:'paste'});return false;}
 return true;
});
document.addEventListener('paste',e=>{e.preventDefault();e.stopImmediatePropagation();post({type:'paste'});},true);
chrome.webview.addEventListener('message', ({data:m}) => {
 if(m.type==='output'){
  const bytes=Uint8Array.from(atob(m.data),c=>c.charCodeAt(0));
  terminal.write(bytes,()=>post({type:'ack',id:m.id}));
 } else if(m.type==='reset'){terminal.reset();fit.fit();terminal.focus();}
});
new ResizeObserver(()=>fit.fit()).observe(document.getElementById('terminal'));
fit.fit(); terminal.focus(); post({type:'ready',cols:terminal.cols,rows:terminal.rows});
