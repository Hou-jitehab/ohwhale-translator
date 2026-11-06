using System;

namespace WT
{
    /// <summary>
    /// 网页版 DeepSeek 的页面结构知识全部集中在这里。
    /// 前端一改版，只改这个文件：改完用「启动自检」确认还能找到元素。
    /// </summary>
    internal static class PageSelectors
    {
        public const string PageUrl = "https://chat.deepseek.com/";

        // 关键元素
        public const string AssistantNode = ".ds-assistant-message-main-content";
        public const string ToggleButton = ".ds-toggle-button";
        public const string ToggleOnClass = "ds-toggle-button--selected";
        public const string PrimaryButton = "ds-button--primary";
        public const string DisabledClass = "ds-button--disabled";
        public const string StopIconPrefix = "M2 4.88";   // 停止按钮的方块图标 path

        public const string ToggleNames = "['深度思考','智能搜索','联网搜索']";

        /// <summary>找输入框下方那个主按钮（发送 / 停止），返回 null 表示找不到。</summary>
        private const string FindPrimaryButtonJs =
@"var box = el.getBoundingClientRect();
  var cands = document.querySelectorAll('div[role=""button""],button');
  var best = null, bestScore = 1e9;
  for (var i = 0; i < cands.length; i++) {
    var b = cands[i].getBoundingClientRect();
    if (b.width < 12 || b.height < 12) continue;
    if (b.y < box.y - 10 || b.y > box.y + box.height + 70) continue;
    if (b.x < box.x) continue;
    var cls = (cands[i].className || '').toString();
    var score = (b.x - box.x) + Math.abs(b.y - box.y) + (/" + PrimaryButton + @"/.test(cls) ? 0 : 800);
    if (score < bestScore) { bestScore = score; best = cands[i]; }
  }";

        public static readonly string StatusJs =
@"(function(){
  var ta = document.querySelector('textarea');
  var ce = document.querySelector('div[contenteditable=""true""]');
  var input = ta || ce;
  var nodes = document.querySelectorAll('" + AssistantNode + @"');
  var last = nodes.length ? (nodes[nodes.length - 1].innerText || '') : '';
  var stop = false;
  var btns = document.querySelectorAll('[role=""button""],button');
  for (var i = 0; i < btns.length; i++) {
    var t = (btns[i].getAttribute('aria-label') || '') + ' ' + (btns[i].innerText || '');
    if (/停止|Stop generating/i.test(t)) { stop = true; break; }
  }
  return { input: !!input, stop: stop, count: nodes.length, text: last };
})()";

        /// <summary>启动自检：确认还能找到输入框、发送按钮、开关。</summary>
        public static readonly string SelfCheckJs =
@"(function(){
  var ta = document.querySelector('textarea');
  var ce = document.querySelector('div[contenteditable=""true""]');
  var input = ta || ce;
  var hasSend = false;
  if (input) {
    var el = input;
    " + FindPrimaryButtonJs + @"
    hasSend = !!best;
  }
  return {
    url: location.href,
    hasInput: !!input,
    hasSend: hasSend,
    toggles: document.querySelectorAll('" + ToggleButton + @"').length,
    nodes: document.querySelectorAll('" + AssistantNode + @"').length
  };
})()";

        /// <summary>点一次停止（空闲时按钮 disabled，等于没点）。</summary>
        public static readonly string StopJs =
@"(function(){
  var ta = document.querySelector('textarea');
  var ce = document.querySelector('div[contenteditable=""true""]');
  var el = ta || ce;
  if (!el) return 'NO_INPUT';
  " + FindPrimaryButtonJs + @"
  if (!best) return 'NO_BTN';
  if (!/" + DisabledClass + @"/.test(best.className || '')) { best.click(); return 'STOPPED'; }
  // 兜底：按停止图标（方块）直接找
  var all = document.querySelectorAll('div[role=""button""],button');
  for (var k = 0; k < all.length; k++) {
    var p = all[k].querySelector('svg path');
    var d = p ? (p.getAttribute('d') || '') : '';
    if (d.indexOf('" + StopIconPrefix + @"') === 0) { all[k].click(); return 'STOPPED_ICON'; }
  }
  return 'IDLE';
})()";

        /// <summary>发送前：关掉深度思考 / 智能搜索等开关。</summary>
        public static readonly string PrepareJs =
@"(function(){
  var names = " + ToggleNames + @";
  var toggles = document.querySelectorAll('" + ToggleButton + @"');
  var off = [];
  for (var i = 0; i < toggles.length; i++) {
    var t = (toggles[i].innerText || '').replace(/\s+/g, '');
    var match = false;
    for (var j = 0; j < names.length; j++) { if (t.indexOf(names[j]) >= 0) { match = true; break; } }
    if (!match) continue;
    var on = toggles[i].getAttribute('aria-pressed') === 'true' ||
             /" + ToggleOnClass + @"/.test(toggles[i].className || '');
    if (on) { toggles[i].click(); off.push(t); }
  }
  return off.length ? ('TOGGLED:' + off.join(',')) : 'TOGGLES_OK';
})()";

        /// <summary>写文字并点发送。textJson 必须是序列化后的 JS 字符串字面量。</summary>
        public static string BuildSendJs(string textJson)
        {
            return
@"(function(text){
  var ta = document.querySelector('textarea');
  var ce = document.querySelector('div[contenteditable=""true""]');
  var el = ta || ce;
  if (!el) return 'NO_INPUT';
  var box = el.getBoundingClientRect();
  el.focus();
  try {
    if (el.tagName === 'TEXTAREA') {
      var setter = Object.getOwnPropertyDescriptor(window.HTMLTextAreaElement.prototype, 'value').set;
      setter.call(el, text);
      el.dispatchEvent(new Event('input', { bubbles: true }));
    } else {
      var sel = window.getSelection();
      var range = document.createRange();
      range.selectNodeContents(el);
      sel.removeAllRanges();
      sel.addRange(range);
      document.execCommand('insertText', false, text);
    }
  } catch (e) { return 'INPUT_ERR:' + e.message; }

  " + FindPrimaryButtonJs + @"
  if (!best || /" + DisabledClass + @"/.test(best.className || '')) return 'NO_SEND_BTN';
  best.click();
  return 'SENT_CLICK';
})(" + textJson + ");";
        }
    }
}