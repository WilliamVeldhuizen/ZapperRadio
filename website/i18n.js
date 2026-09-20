// Shows the page in the language of the visitor's browser. The English texts are the page itself, so it reads
// fine without JavaScript and search engines get real content. Every other language is i18n/<code>.json,
// a flat map from the data-i18n keys in index.html to the translated (trusted, first-party) HTML, loaded
// only when it is needed. The languages and the way one is picked follow the app (Localizer.cs).
(function () {
  var STORE = 'zapperradio-lang';
  var LANGS = [
    { code: 'en', name: 'English', html: 'en' },
    { code: 'nl', name: 'Nederlands', html: 'nl' },
    { code: 'de', name: 'Deutsch', html: 'de' },
    { code: 'fr', name: 'Français', html: 'fr' },
    { code: 'es', name: 'Español', html: 'es' },
    { code: 'it', name: 'Italiano', html: 'it' },
    { code: 'pt-BR', name: 'Português (Brasil)', html: 'pt-BR' },
    { code: 'uk', name: 'Українська', html: 'uk' },
    { code: 'zh-CN', name: '中文 (简体)', html: 'zh-Hans' },
    { code: 'ja', name: '日本語', html: 'ja' }
  ];

  var root = document.documentElement;
  var cache = {};
  var dict = {};
  var current = 'en';
  var english = {}; // the texts of the page itself, kept so English can be restored

  function known(code) {
    return LANGS.some(function (l) { return l.code === code; });
  }

  // The language a browser language falls under: "nl-BE" is Dutch and "pt-PT" is Portuguese. Chinese is the one
  // where the script matters: only simplified characters are translated, so zh-TW and zh-Hant fall through.
  function match(tag) {
    var parts = String(tag).split('-').map(function (p) { return p.toLowerCase(); });
    if (parts[0] === 'zh') {
      var has = function (p) { return parts.indexOf(p) >= 0; };
      var simplified = parts.length === 1 || has('hans') || (!has('hant') && (has('cn') || has('sg')));
      return simplified ? 'zh-CN' : null;
    }
    for (var i = 0; i < LANGS.length; i++) {
      if (LANGS[i].code.split('-')[0].toLowerCase() === parts[0]) return LANGS[i].code;
    }
    return null;
  }

  function stored() {
    try { return localStorage.getItem(STORE); } catch (e) { return null; }
  }

  // ?lang=nl (for links and testing), then the visitor's earlier choice, then the first browser language we speak.
  function initial() {
    try {
      var asked = new URLSearchParams(location.search).get('lang');
      if (asked && match(asked)) return match(asked);
    } catch (e) {}
    var saved = stored();
    if (known(saved)) return saved;
    var prefs = navigator.languages && navigator.languages.length ? navigator.languages : [navigator.language];
    var traditional = false;
    for (var i = 0; i < prefs.length; i++) {
      var tag = prefs[i];
      if (!tag) continue;
      var m = match(tag);
      // Browsers append the bare language to a regional one ("zh-TW" arrives as "zh-TW", "zh"). That "zh" is not
      // a request for simplified Chinese from someone who asked for traditional.
      if (m === 'zh-CN' && traditional && tag.indexOf('-') < 0) continue;
      if (m) return m;
      if (/^zh(-|$)/i.test(tag)) traditional = true;
    }
    return 'en';
  }

  function load(code) {
    if (code === 'en') return Promise.resolve({});
    if (!cache[code]) {
      cache[code] = fetch('i18n/' + code + '.json').then(function (r) {
        if (!r.ok) throw new Error(r.status);
        return r.json();
      });
    }
    return cache[code];
  }

  function original(el, prop, read) {
    var bag = english[prop] || (english[prop] = new Map());
    if (!bag.has(el)) bag.set(el, read());
    return bag.get(el);
  }

  function setMeta(selector, attr, text) {
    var el = document.querySelector(selector);
    if (!el) return;
    el.setAttribute(attr, original(el, attr, function () { return el.getAttribute(attr); }));
    if (text) el.setAttribute(attr, text);
  }

  function apply(code, texts) {
    current = code;
    dict = texts;
    root.lang = LANGS.filter(function (l) { return l.code === code; })[0].html;

    document.querySelectorAll('[data-i18n]').forEach(function (el) {
      var en = original(el, 'html', function () { return el.innerHTML; });
      var text = texts[el.getAttribute('data-i18n')];
      el.innerHTML = text !== undefined ? text : en;
    });
    document.querySelectorAll('[data-i18n-aria]').forEach(function (el) {
      var en = original(el, 'aria', function () { return el.getAttribute('aria-label'); });
      var text = texts[el.getAttribute('data-i18n-aria')];
      el.setAttribute('aria-label', text !== undefined ? text : en);
    });

    var title = original(document, 'title', function () { return document.title; });
    document.title = texts.title !== undefined ? texts.title : title;
    setMeta('meta[name="description"]', 'content', texts.description);

    document.querySelectorAll('.chips li[data-code]').forEach(function (li) {
      li.classList.toggle('current', li.getAttribute('data-code') === code);
    });
    var select = document.getElementById('lang-select');
    if (select) select.value = code;
    root.classList.remove('i18n-pending');
  }

  function use(code) {
    return load(code).then(
      function (texts) { apply(code, texts); },
      function () { apply('en', {}); }
    );
  }

  function buildPicker() {
    var select = document.getElementById('lang-select');
    if (!select) return;
    LANGS.forEach(function (l) {
      var option = document.createElement('option');
      option.value = l.code;
      option.lang = l.html;
      option.textContent = l.name;
      select.appendChild(option);
    });
    select.value = current;
    select.addEventListener('change', function () {
      try { localStorage.setItem(STORE, select.value); } catch (e) {}
      use(select.value);
    });
    select.parentNode.hidden = false;
  }

  function ready(fn) {
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', fn);
    else fn();
  }

  window.zr = {
    /** The translated text for a key, or the fallback (English) when there is none. */
    t: function (key, fallback) { return dict[key] !== undefined ? dict[key] : fallback; },
    get lang() { return current; }
  };

  // The file is fetched right away, while the rest of the page is still being parsed, and the texts are put in
  // once both are there.
  var start = initial();
  var fetched = load(start);
  if (start !== 'en') {
    // Keep the page hidden until the translation is in, but never for long if the file is slow.
    root.classList.add('i18n-pending');
    setTimeout(function () { root.classList.remove('i18n-pending'); }, 3000);
  }
  ready(function () {
    fetched.then(
      function (texts) { apply(start, texts); },
      function () { apply('en', {}); } // a missing file leaves the page in English rather than half done
    ).then(buildPicker);
  });
})();
