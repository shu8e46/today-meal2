// 오늘급식2 — 웹 / 안드로이드 공용. 윈도우 버전(Windows\MainWindow.xaml.cs)과 같은 동작을 한다.
(function () {
  'use strict';

  // ───────────────────────── 날짜 ─────────────────────────

  var DOW = ['일', '월', '화', '수', '목', '금', '토'];

  function day(y, m, d) { return new Date(y, m, d); }
  function todayDate() { var n = new Date(); return day(n.getFullYear(), n.getMonth(), n.getDate()); }
  function addDays(d, n) { return day(d.getFullYear(), d.getMonth(), d.getDate() + n); }
  function addMonths(d, n) { return day(d.getFullYear(), d.getMonth() + n, 1); }
  function firstOfMonth(d) { return day(d.getFullYear(), d.getMonth(), 1); }
  function key(d) { return d.getFullYear() * 10000 + (d.getMonth() + 1) * 100 + d.getDate(); }
  function monthId(d) { return d.getFullYear() * 100 + d.getMonth() + 1; }
  function same(a, b) { return a && b && key(a) === key(b); }
  function isWeekend(d) { return d.getDay() === 0 || d.getDay() === 6; }
  function ymd(d) { return String(key(d)); }
  function longDate(d) { return (d.getMonth() + 1) + '월 ' + d.getDate() + '일 ' + DOW[d.getDay()] + '요일'; }
  function daysBetween(a, b) { return Math.round((b - a) / 86400000); }

  function parseYmd(s) {
    if (!/^\d{8}$/.test(s || '')) return null;
    return day(+s.slice(0, 4), +s.slice(4, 6) - 1, +s.slice(6, 8));
  }

  // ───────────────────────── 나이스 (open.neis.go.kr) ─────────────────────────

  var KEY = '6e2bad39ab46406e9bd1e39183850d27';
  var BASE = 'https://open.neis.go.kr/hub/';
  var PAGE = 1000;

  // "돈육김치찌개 (5.9.10.13)" 처럼 붙어 오는 알레르기 번호
  var ALLERGY_PAREN = /\(\s*\d{1,2}(\s*[.,]\s*\d{1,2})*\s*[.,]?\s*\)/g;
  // 괄호 없이 "돈육김치찌개5.9.10.13." 으로 오는 학교도 있다
  var ALLERGY_TAIL = /(\d{1,2}\.)+\d{0,2}\s*$/;

  function NeisError(message) { this.message = message; this.neis = true; }

  function describe(ex) {
    if (ex && ex.neis) return ex.message;
    return '불러오지 못했습니다. 인터넷 연결을 확인한 뒤 새로고침을 눌러 주세요.';
  }

  function getJson(url) {
    var ctl = typeof AbortController !== 'undefined' ? new AbortController() : null;
    var timer = setTimeout(function () { if (ctl) ctl.abort(); }, 15000);
    return fetch(url, ctl ? { signal: ctl.signal } : undefined)
      .then(function (r) {
        if (!r.ok) throw new Error('http ' + r.status);
        return r.json();
      })
      .finally(function () { clearTimeout(timer); });
  }

  // 정상:   {"서비스명":[{"head":[{"list_total_count":N},{"RESULT":{..}}]},{"row":[..]}]}
  // 그 외:  {"RESULT":{"CODE":"INFO-200","MESSAGE":"해당하는 데이터가 없습니다."}}
  function readRows(root, service) {
    if (!root || typeof root !== 'object') throw new NeisError('나이스 응답을 읽을 수 없습니다.');
    var svc = root[service];
    if (!svc) {
      var res = root.RESULT;
      if (res) {
        if (res.CODE === 'INFO-200') return { rows: [], total: 0 };
        throw new NeisError('나이스: ' + (res.MESSAGE || ''));
      }
      throw new NeisError('나이스 응답을 읽을 수 없습니다.');
    }
    var out = { rows: [], total: 0 };
    if (!Array.isArray(svc)) return out;
    svc.forEach(function (part) {
      if (!part) return;
      if (Array.isArray(part.head)) {
        part.head.forEach(function (h) {
          if (h && h.list_total_count != null) out.total = +h.list_total_count;
        });
      }
      if (Array.isArray(part.row)) out.rows = out.rows.concat(part.row);
    });
    return out;
  }

  var decoder = document.createElement('textarea');
  function htmlDecode(s) { decoder.innerHTML = s; return decoder.value; }

  function cleanDish(raw) {
    return htmlDecode(raw).replace(ALLERGY_PAREN, '').replace(ALLERGY_TAIL, '').replace(/\s{2,}/g, ' ').trim();
  }

  function parseMeal(r) {
    var date = parseYmd(r.MLSV_YMD);
    if (!date) return null;
    var type = parseInt(r.MMEAL_SC_CODE, 10);
    var meal = {
      date: date,
      type: type >= 1 && type <= 3 ? type : 2,
      typeName: r.MMEAL_SC_NM || '',
      calories: String(r.CAL_INFO || '').replace('Kcal', 'kcal').trim(),
      dishes: []
    };
    String(r.DDISH_NM || '').split(/<br\s*\/?>/i).forEach(function (raw) {
      var dish = cleanDish(raw);
      if (dish) meal.dishes.push(dish);
    });
    return meal.dishes.length ? meal : null;
  }

  function searchSchools(name) {
    var url = BASE + 'schoolInfo?KEY=' + KEY + '&Type=json&pIndex=1&pSize=50&SCHUL_NM=' + encodeURIComponent(name);
    return getJson(url).then(function (json) {
      var list = [];
      readRows(json, 'schoolInfo').rows.forEach(function (r) {
        var s = {
          officeCode: r.ATPT_OFCDC_SC_CODE || '',
          officeName: r.ATPT_OFCDC_SC_NM || '',
          code: r.SD_SCHUL_CODE || '',
          name: r.SCHUL_NM || '',
          address: r.ORG_RDNMA || ''
        };
        if (s.code && s.officeCode) list.push(s);
      });
      return list;
    });
  }

  async function getMeals(school, from, to) {
    var meals = [];
    for (var page = 1; ; page++) {
      var url = BASE + 'mealServiceDietInfo?KEY=' + KEY + '&Type=json&pIndex=' + page + '&pSize=' + PAGE
        + '&ATPT_OFCDC_SC_CODE=' + encodeURIComponent(school.officeCode)
        + '&SD_SCHUL_CODE=' + encodeURIComponent(school.code)
        + '&MLSV_FROM_YMD=' + ymd(from) + '&MLSV_TO_YMD=' + ymd(to);
      var got = readRows(await getJson(url), 'mealServiceDietInfo');
      got.rows.forEach(function (r) {
        var m = parseMeal(r);
        if (m) meals.push(m);
      });
      if (got.rows.length === 0 || page * PAGE >= got.total) break;
    }
    return meals;
  }

  // ───────────────────────── 받아 온 급식 기억 (달 단위) ─────────────────────────

  var store = {
    school: null,
    byDate: {},
    loaded: {},
    generation: 0,

    setSchool: function (s) { this.school = s; this.clear(); },
    clear: function () { this.generation++; this.byDate = {}; this.loaded = {}; },
    isLoaded: function (d) { return !!this.loaded[monthId(d)]; },

    // from ~ to 가 걸친 달을 전부 받아 둔다. 이미 있으면 아무것도 안 한다.
    ensure: async function (from, to) {
      if (!this.school) return;
      var needFrom = null, needTo = null;
      var last = firstOfMonth(to);
      for (var m = firstOfMonth(from); m <= last; m = addMonths(m, 1)) {
        if (this.loaded[monthId(m)]) continue;
        if (!needFrom) needFrom = m;
        needTo = m;
      }
      if (!needFrom) return;

      var gen = this.generation;
      var a = needFrom, b = addDays(addMonths(needTo, 1), -1);
      var meals = await getMeals(this.school, a, b);
      if (gen !== this.generation) return;   // 그 사이 학교가 바뀌었거나 새로고침됨

      var d;
      for (d = a; d <= b; d = addDays(d, 1)) delete this.byDate[key(d)];
      var byDate = this.byDate;
      meals.forEach(function (meal) {
        var k = key(meal.date);
        (byDate[k] || (byDate[k] = [])).push(meal);
      });
      for (var k in byDate) byDate[k].sort(function (x, y) { return x.type - y.type; });
      for (m = a; m <= b; m = addMonths(m, 1)) this.loaded[monthId(m)] = true;
    },

    // 그날의 급식 전부 (조식, 중식, 석식 순). 없으면 빈 목록.
    get: function (d) { return this.byDate[key(d)] || []; },

    // 원하는 종류가 있으면 그것, 없으면 중식, 그것도 없으면 그날 첫 급식.
    pick: function (d, preferred) {
      var list = this.get(d), i;
      if (!list.length) return null;
      for (i = 0; i < list.length; i++) if (list[i].type === preferred) return list[i];
      for (i = 0; i < list.length; i++) if (list[i].type === 2) return list[i];
      return list[0];
    }
  };

  // ───────────────────────── 학교·테마 저장 ─────────────────────────
  // 웹은 쿠키, 안드로이드 앱은 앱 저장소(TodayMealHost)에 넣는다.

  var host = window.TodayMealHost || null;
  var COOKIE = 'todaymeal2';

  var settings = {
    officeCode: '', officeName: '', schoolCode: '', schoolName: '', schoolAddress: '',
    theme: 'light', mealType: 2,
    volatile: false   // 주소에 ?school= 을 붙여 연 경우: 저장하지 않는다
  };

  function hasSchool() { return !!(settings.schoolCode && settings.officeCode); }

  function toSchool() {
    return {
      officeCode: settings.officeCode, officeName: settings.officeName,
      code: settings.schoolCode, name: settings.schoolName, address: settings.schoolAddress
    };
  }

  function setSchool(s) {
    settings.officeCode = s.officeCode;
    settings.officeName = s.officeName;
    settings.schoolCode = s.code;
    settings.schoolName = s.name;
    settings.schoolAddress = s.address;
  }

  function readSaved() {
    try {
      if (host) return host.load() || '';
      var m = document.cookie.match(new RegExp('(?:^|; )' + COOKIE + '=([^;]*)'));
      if (m) return decodeURIComponent(m[1]);
      return localStorage.getItem(COOKIE) || '';   // 쿠키가 막힌 환경
    } catch (e) {
      return '';
    }
  }

  function loadSettings() {
    try {
      var d = JSON.parse(readSaved() || '{}');
      ['officeCode', 'officeName', 'schoolCode', 'schoolName', 'schoolAddress'].forEach(function (k) {
        if (typeof d[k] === 'string') settings[k] = d[k];
      });
      if (d.theme === 'dark') settings.theme = 'dark';
      if (d.mealType >= 1 && d.mealType <= 3) settings.mealType = d.mealType;
    } catch (e) {
      // 저장된 값이 깨졌으면 처음 켠 것처럼 시작한다
    }
  }

  function saveSettings() {
    if (settings.volatile) return;
    var json = JSON.stringify({
      officeCode: settings.officeCode, officeName: settings.officeName,
      schoolCode: settings.schoolCode, schoolName: settings.schoolName, schoolAddress: settings.schoolAddress,
      theme: settings.theme, mealType: settings.mealType
    });
    try {
      if (host) { host.save(json); return; }
      document.cookie = COOKIE + '=' + encodeURIComponent(json) + '; max-age=34560000; path=/; SameSite=Lax';
      localStorage.setItem(COOKIE, json);
    } catch (e) {
      // 저장 실패는 앱을 멈출 일이 아니다. 다음에 다시 고르면 된다.
    }
  }

  function applyTheme() {
    var dark = settings.theme === 'dark';
    document.documentElement.setAttribute('data-theme', dark ? 'dark' : 'light');
    var meta = document.querySelector('meta[name="theme-color"]');
    if (meta) meta.setAttribute('content', dark ? '#141413' : '#FFFFFF');
    $('themeLight').classList.toggle('on', !dark);
    $('themeDark').classList.toggle('on', dark);
    try { if (host) host.setTheme(dark); } catch (e) { /* 색만 못 바꿀 뿐이다 */ }
  }

  // ───────────────────────── 화면 공통 ─────────────────────────

  function $(id) { return document.getElementById(id); }

  function el(tag, cls, text) {
    var e = document.createElement(tag);
    if (cls) e.className = cls;
    if (text != null) e.textContent = text;
    return e;
  }

  function clear(node) { while (node.firstChild) node.removeChild(node.firstChild); }

  var MONTH_MAX_LINES = 9;     // 달력 칸 하나에 보여 줄 메뉴 줄 수
  var HERO_FONT = 60;          // 가운데 메뉴 기준 크기 (화면에 맞춰 늘거나 줄어든다)
  var HERO_MAX_LINE = 104;     // 한 줄이 이보다 커지지는 않게

  var mode = 'setup';          // setup | day | month | year
  var today = todayDate();
  var fixedToday = false;      // 주소로 날짜를 고정했는지 (화면 확인용)
  var selected = today;        // 하루 화면에서 보고 있는 날
  var monthCursor = firstOfMonth(today);
  var yearCursor = today.getFullYear();
  var loadSeq = 0;             // 늦게 도착한 응답을 버리기 위한 번호
  var openPanel = null;        // null | 'menu' | 'settings'
  var stripDays = [];
  var stripCells = [];
  var yearCells = [];
  var yearHover = null;
  var lastPointer = 'mouse';

  function nameOf(m) {
    if (m.typeName) return m.typeName;
    return m.type === 1 ? '조식' : m.type === 3 ? '석식' : '중식';
  }

  function setMode(m) {
    mode = m;
    $('setupView').hidden = m !== 'setup';
    $('dayView').hidden = m !== 'day';
    $('monthView').hidden = m !== 'month';
    $('yearView').hidden = m !== 'year';

    $('navToday').classList.toggle('on', m === 'day');
    $('navMonth').classList.toggle('on', m === 'month');
    $('navYear').classList.toggle('on', m === 'year');

    var none = !hasSchool();
    $('navToday').disabled = none;
    $('navMonth').disabled = none;
    $('navYear').disabled = none;
    $('refreshBtn').disabled = none;
  }

  function setBusy(busy) { $('refreshBtn').classList.toggle('busy', busy); }

  function showSchool() {
    $('schoolLabel').textContent = settings.schoolName;
    $('currentSchool').textContent = hasSchool() ? settings.schoolName : '아직 고르지 않았습니다';
    var sub = settings.schoolAddress || settings.officeName;
    $('currentSchoolSub').textContent = sub;
    $('currentSchoolSub').hidden = !sub;
  }

  // types: { 1: '조식', 2: '중식', ... }
  function fillTypeToggles(hostEl, types, current) {
    clear(hostEl);
    var keys = Object.keys(types).map(Number).sort();
    if (keys.length < 2) return;   // 한 끼만 주는 학교면 고를 것이 없다
    keys.forEach(function (type) {
      var b = el('button', type === current ? 'on' : '', types[type]);
      b.onclick = function () {
        settings.mealType = type;
        saveSettings();
        if (mode === 'month') buildMonth();
        else {
          buildStrip();
          renderDay();
        }
      };
      hostEl.appendChild(b);
    });
  }

  function dishLines(parent, meal, maxLines) {
    var shown = meal.dishes.length > maxLines ? maxLines - 1 : meal.dishes.length;
    for (var i = 0; i < shown; i++) parent.appendChild(el('div', 'dish', meal.dishes[i]));
    if (shown < meal.dishes.length) parent.appendChild(el('div', 'dish more', '외 ' + (meal.dishes.length - shown) + '개'));
  }

  // ───────────────────────── 하루 화면 ─────────────────────────

  async function showDay(date) {
    selected = date;
    setMode('day');
    var seq = ++loadSeq;

    // 위쪽 5칸을 채우려면 이번 달과 다음 달이 필요하다
    var from = firstOfMonth(today);
    var to = addDays(addMonths(from, 2), -1);
    var cached = store.isLoaded(from) && store.isLoaded(addMonths(from, 1)) && store.isLoaded(selected);
    if (!cached) {
      clearStrip();
      $('dayTitle').textContent = titleFor(selected);
      clear($('dayTypes'));
      $('dayCalories').textContent = '';
      sayDay('불러오는 중…');
    }

    setBusy(true);
    try {
      await store.ensure(from, to);
      await store.ensure(selected, selected);
      if (seq !== loadSeq) return;
      buildStrip();
      renderDay();
    } catch (ex) {
      if (seq !== loadSeq) return;
      clearStrip();
      clear($('dayTypes'));
      $('dayCalories').textContent = '';
      sayDay(describe(ex));
    } finally {
      if (seq === loadSeq) setBusy(false);
    }
  }

  function titleFor(d) {
    var title = longDate(d);
    if (same(d, today)) return '오늘 · ' + title;
    if (same(d, addDays(today, 1))) return '내일 · ' + title;
    return title;
  }

  function sayDay(text) {
    $('dishList').hidden = true;
    $('dayMessage').textContent = text;
    $('dayMessage').hidden = false;
  }

  function clearStrip() {
    clear($('strip'));
    stripDays = [];
    stripCells = [];
  }

  // 첫 칸은 오늘, 나머지는 급식이 있는 날만 골라서 4개. 칸에는 날짜와 요일만 넣는다.
  function buildStrip() {
    clearStrip();
    stripDays.push(today);
    var end = addMonths(firstOfMonth(today), 2);
    for (var d = addDays(today, 1); d < end && stripDays.length < 5; d = addDays(d, 1)) {
      if (store.get(d).length > 0) stripDays.push(d);
    }
    for (var i = 0; i < 5; i++) {
      if (i < stripDays.length) $('strip').appendChild(makeStripCell(stripDays[i]));
      else $('strip').appendChild(el('div', 'stripCell'));
    }
  }

  function makeStripCell(date) {
    var cell = el('button', 'stripCell');
    if (!store.pick(date, settings.mealType)) cell.classList.add('none');
    cell.appendChild(el('span', 'date', (date.getMonth() + 1) + '.' + date.getDate()));
    cell.appendChild(el('span', 'dow', DOW[date.getDay()]));
    var mark = same(date, today) ? '오늘' : same(date, addDays(today, 1)) ? '내일' : '';
    if (mark) cell.appendChild(el('span', 'mark', mark));
    cell.onclick = function () {
      selected = date;
      renderDay();
    };
    stripCells.push(cell);
    return cell;
  }

  function markStrip() {
    stripCells.forEach(function (cell, i) { cell.classList.toggle('on', same(stripDays[i], selected)); });
  }

  function renderDay() {
    var meals = store.get(selected);
    var meal = store.pick(selected, settings.mealType);

    var title = titleFor(selected);
    if (meals.length === 1 && meal.type !== 2) title += ' · ' + nameOf(meal);
    $('dayTitle').textContent = title;

    var types = {};
    meals.forEach(function (m) { types[m.type] = nameOf(m); });
    fillTypeToggles($('dayTypes'), types, meal ? meal.type : 0);

    var list = $('dishList');
    clear(list);
    if (!meal) {
      $('dayCalories').textContent = '';
      sayDay(same(selected, today) ? '오늘은 급식이 없습니다' : '급식이 없는 날입니다');
    } else {
      meal.dishes.forEach(function (dish) { list.appendChild(el('div', '', dish)); });
      list.hidden = false;
      $('dayMessage').hidden = true;
      $('dayCalories').textContent = meal.calories;
      fitHero();
    }
    markStrip();
  }

  // 가운데 메뉴를 남는 자리에 꽉 차게 키우거나 줄인다
  function fitHero() {
    var list = $('dishList'), box = $('dishBox');
    if (mode !== 'day' || list.hidden || !list.children.length) return;
    list.style.fontSize = HERO_FONT + 'px';
    var w = list.offsetWidth, h = list.offsetHeight;
    if (!w || !h || !box.clientWidth || !box.clientHeight) return;
    var scale = Math.min(box.clientWidth / w, box.clientHeight / h, list.children.length * HERO_MAX_LINE / h);
    list.style.fontSize = Math.max(8, Math.floor(HERO_FONT * scale * 10) / 10) + 'px';
  }

  // ← → 키, 좌우로 밀기: 급식이 있는 앞뒤 날로
  function stepDay(direction) {
    var d = selected;
    for (var i = 0; i < 45; i++) {
      d = addDays(d, direction);
      if (same(d, today) || store.get(d).length > 0) {
        if (!store.isLoaded(d)) return;
        selected = d;
        renderDay();
        return;
      }
    }
  }

  // ───────────────────────── 달력 화면 ─────────────────────────

  async function showMonth(month) {
    monthCursor = firstOfMonth(month);
    setMode('month');
    $('monthTitle').textContent = monthCursor.getFullYear() + '년 ' + (monthCursor.getMonth() + 1) + '월';
    var seq = ++loadSeq;

    if (!store.isLoaded(monthCursor)) {
      clearMonth();
      sayMonth('불러오는 중…');
    }

    setBusy(true);
    try {
      await store.ensure(monthCursor, monthCursor);
      if (seq !== loadSeq) return;
      buildMonth();
    } catch (ex) {
      if (seq !== loadSeq) return;
      clearMonth();
      sayMonth(describe(ex));
    } finally {
      if (seq === loadSeq) setBusy(false);
    }
  }

  function clearMonth() {
    ['monthGrid', 'monthHead', 'monthTypes', 'monthMini', 'monthList'].forEach(function (id) { clear($(id)); });
    $('monthMessage').hidden = true;
    $('monthBody').classList.remove('empty');
  }

  function sayMonth(text) {
    $('monthMessage').textContent = text;
    $('monthMessage').hidden = false;
    $('monthBody').classList.add('empty');
  }

  function weekStart(d, sundayFirst) {
    var back = sundayFirst ? d.getDay() : (d.getDay() + 6) % 7;
    return addDays(d, -back);
  }

  function buildMonth() {
    clearMonth();
    var first = monthCursor;
    var last = addDays(addMonths(monthCursor, 1), -1);

    var types = {};
    var weekend = false, mealDays = 0, d;
    for (d = first; d <= last; d = addDays(d, 1)) {
      var meals = store.get(d);
      meals.forEach(function (m) { types[m.type] = nameOf(m); });
      if (!meals.length) continue;
      mealDays++;
      if (isWeekend(d)) weekend = true;
    }
    if (mealDays === 0) {
      sayMonth('이 달에는 급식 정보가 없습니다');
      return;
    }

    var current = types[settings.mealType] ? settings.mealType : 2;
    fillTypeToggles($('monthTypes'), types, current);

    // ── 넓은 화면용 달력. 주말 급식이 없는 달은 월~금 다섯 칸만 그려서 칸을 넓게 쓴다
    var names = weekend ? DOW : ['월', '화', '수', '목', '금'];
    var cols = names.length;
    $('monthHead').style.gridTemplateColumns = 'repeat(' + cols + ', 1fr)';
    $('monthGrid').style.gridTemplateColumns = 'repeat(' + cols + ', 1fr)';
    names.forEach(function (n) { $('monthHead').appendChild(el('div', '', n)); });

    var firstShown = first, lastShown = last;
    if (!weekend) {
      while (isWeekend(firstShown)) firstShown = addDays(firstShown, 1);
      while (isWeekend(lastShown)) lastShown = addDays(lastShown, -1);
    }
    var origin = weekStart(firstShown, weekend);
    var rows = daysBetween(origin, weekStart(lastShown, weekend)) / 7 + 1;

    // 각 주는 그 주에서 가장 긴 메뉴가 들어갈 만큼은 확보하고, 화면이 크면 남는 높이를 똑같이 나눠 갖는다
    var rowLines = [], r, c;
    for (r = 0; r < rows; r++) rowLines.push(0);
    for (d = firstShown; d <= lastShown; d = addDays(d, 1)) {
      var meal = store.pick(d, settings.mealType);
      if (!meal || (!weekend && isWeekend(d))) continue;
      r = daysBetween(origin, weekStart(d, weekend)) / 7;
      rowLines[r] = Math.max(rowLines[r], Math.min(meal.dishes.length, MONTH_MAX_LINES));
    }
    var lineH = parseFloat(getComputedStyle(document.body).fontSize) > 14 ? 21 : 18;
    $('monthGrid').style.gridTemplateRows = rowLines.map(function (n) {
      return 'minmax(' + (46 + n * lineH) + 'px, 1fr)';
    }).join(' ');

    var todayCell = null;
    for (r = 0; r < rows; r++) {
      for (c = 0; c < cols; c++) {
        d = addDays(origin, r * 7 + c);
        var cell = makeMonthCell(d, d >= first && d <= last, c, r === rows - 1);
        $('monthGrid').appendChild(cell);
        if (same(d, today)) todayCell = cell;
      }
    }

    // ── 좁은 화면용: 작은 달력 + 날짜별 목록
    $('monthMini').appendChild(makeMiniMonth(first.getFullYear(), first.getMonth(), false, function (cell) {
      showDay(cell.date);
    }));
    var todayRow = null;
    for (d = first; d <= last; d = addDays(d, 1)) {
      var row = makeListRow(d);
      if (!row) continue;
      $('monthList').appendChild(row);
      if (!todayRow && d >= today) todayRow = row;
    }

    // 달력이 화면보다 길어서 오늘이 가려지면, 오늘이 보이게 내려 둔다
    $('monthScroll').scrollTop = 0;
    $('monthList').scrollTop = 0;
    if (todayCell && todayCell.offsetParent) {
      var over = todayCell.offsetTop + todayCell.offsetHeight - $('monthScroll').clientHeight;
      if (over > 0) $('monthScroll').scrollTop = Math.min(todayCell.offsetTop, over);
    }
    if (todayRow && todayRow.offsetParent) $('monthList').scrollTop = todayRow.offsetTop - $('monthList').offsetTop;
  }

  function makeMonthCell(date, inMonth, column, lastRow) {
    var outer = el('div', 'monthCell' + (column === 0 ? ' first' : '') + (lastRow ? ' last' : ''));
    if (!inMonth) return outer;

    var meal = store.pick(date, settings.mealType);
    var past = date < today;
    var inner = el('div', 'cellIn' + (past ? ' past' : ''));

    if (same(date, today)) inner.appendChild(el('div', 'chip', date.getDate()));
    else inner.appendChild(el('div', 'num' + (!meal || past ? ' faint' : ''), date.getDate()));

    if (!meal) {
      outer.appendChild(inner);
      return outer;
    }

    dishLines(inner, meal, MONTH_MAX_LINES);
    var button = el('button', 'cellBtn');
    button.appendChild(inner);
    button.onclick = function () { showDay(date); };
    outer.appendChild(button);
    return outer;
  }

  function makeListRow(date) {
    var meal = store.pick(date, settings.mealType);
    if (!meal) return null;
    var row = el('button', 'listRow' + (date < today ? ' past' : ''));
    var when = el('div', 'when');
    when.appendChild(el('span', same(date, today) ? 'chip' : 'day', date.getDate()));
    when.appendChild(el('span', 'dow', DOW[date.getDay()]));
    row.appendChild(when);
    row.appendChild(el('div', 'dishes', meal.dishes.join(', ')));
    row.onclick = function () { showDay(date); };
    return row;
  }

  // ───────────────────────── 올해 화면 ─────────────────────────

  async function showYear(year) {
    yearCursor = Math.max(2000, Math.min(2099, year));
    setMode('year');
    $('yearTitle').textContent = yearCursor + '년';
    var seq = ++loadSeq;

    var from = day(yearCursor, 0, 1), to = day(yearCursor, 11, 31);
    var cached = true;
    for (var m = from; m <= to; m = addMonths(m, 1)) if (!store.isLoaded(m)) cached = false;
    if (!cached) {
      clearYear();
      sayYear('불러오는 중…');
    }

    setBusy(true);
    try {
      await store.ensure(from, to);
      if (seq !== loadSeq) return;
      buildYear();
    } catch (ex) {
      if (seq !== loadSeq) return;
      clearYear();
      sayYear(describe(ex));
    } finally {
      if (seq === loadSeq) setBusy(false);
    }
  }

  function clearYear() {
    clear($('yearGrid'));
    yearCells = [];
    clear($('yearDetail'));
    clear($('yearResults'));
    $('yearStat').textContent = '';
    $('yearMessage').hidden = true;
  }

  function sayYear(text) {
    $('yearMessage').textContent = text;
    $('yearMessage').hidden = false;
  }

  function buildYear() {
    clearYear();
    for (var month = 0; month < 12; month++) {
      $('yearGrid').appendChild(makeMiniMonth(yearCursor, month, true, onYearCell, yearCells));
    }

    var mealDays = 0;
    yearCells.forEach(function (c) { if (c.meals.length > 0) mealDays++; });
    $('yearStat').textContent = mealDays > 0 ? '급식 있는 날 ' + mealDays + '일' : '급식 정보 없음';

    // 옆 칸에는 일단 오늘 급식을 띄워 둔다
    if (!yearHover || yearHover.getFullYear() !== yearCursor || store.get(yearHover).length === 0) {
      yearHover = today.getFullYear() === yearCursor && store.get(today).length > 0 ? today : null;
    }
    renderYearDetail();
    applyYearSearch();
  }

  // 마우스: 올리면 옆에 그날 급식, 누르면 크게. 손가락: 한 번 누르면 옆에, 한 번 더 누르면 크게.
  function onYearCell(cell) {
    if (lastPointer === 'mouse' || same(yearHover, cell.date)) {
      showDay(cell.date);
      return;
    }
    yearHover = cell.date;
    renderYearDetail();
  }

  // 작은 달 하나 (일요일 시작). collect 를 주면 만든 칸을 거기에 모은다.
  function makeMiniMonth(year, month, withTitle, onPick, collect) {
    var first = day(year, month, 1);
    var wrap = el('div', 'mini');

    if (withTitle) {
      var title = el('button', 'title link', (month + 1) + '월');
      title.onclick = function () { showMonth(first); };
      wrap.appendChild(title);
    }

    var grid = el('div', 'miniGrid');
    for (var i = 0; i < first.getDay(); i++) grid.appendChild(el('div'));
    var days = day(year, month + 1, 0).getDate();
    for (var n = 1; n <= days; n++) {
      (function (date) {
        var box = el('div', '', date.getDate());
        var cell = { date: date, box: box, meals: store.get(date) };
        if (same(date, today)) box.classList.add('today');
        if (cell.meals.length > 0) {
          box.classList.add('has');
          box.onmouseenter = function () {
            if (!collect || lastPointer !== 'mouse') return;
            yearHover = cell.date;
            renderYearDetail();
          };
          box.onclick = function () { onPick(cell); };
        }
        if (collect) collect.push(cell);
        grid.appendChild(box);
      })(day(year, month, n));
    }
    wrap.appendChild(grid);
    return wrap;
  }

  // 옆 칸 위쪽: 마우스를 올린(또는 누른) 날의 급식
  function renderYearDetail() {
    var detail = $('yearDetail');
    clear(detail);
    var searching = $('yearSearch').value.trim().length > 0;
    $('yearResults').classList.toggle('top', !yearHover && searching);
    yearCells.forEach(function (c) { c.box.classList.toggle('sel', same(c.date, yearHover)); });

    if (!yearHover) {
      if (searching) return;   // 검색 중에는 결과 목록이 맨 위로 온다
      detail.appendChild(el('div', 'hint', lastPointer === 'mouse'
        ? '색이 칠해진 날이 급식 있는 날입니다. 날짜에 마우스를 올리면 그날 급식이 여기에 나오고, 누르면 크게 봅니다.'
        : '색이 칠해진 날이 급식 있는 날입니다. 날짜를 누르면 그날 급식이 여기에 나옵니다.'));
      return;
    }

    var date = yearHover;
    var meals = store.get(date);
    detail.appendChild(el('div', 'date', longDate(date)));
    meals.forEach(function (m) {
      detail.appendChild(el('div', 'kind' + (meals.length > 1 ? '' : ' single'), meals.length > 1 ? nameOf(m) : ''));
      m.dishes.forEach(function (dish) { detail.appendChild(el('div', 'dish', dish)); });
      if (m.calories) detail.appendChild(el('div', 'kcal', m.calories));
    });
    var big = el('button', 'big link', '크게 보기');
    big.onclick = function () { showDay(date); };
    detail.appendChild(big);
  }

  function matchingDishes(meals, q) {
    var found = [];
    q = q.toLowerCase();
    meals.forEach(function (m) {
      m.dishes.forEach(function (dish) {
        if (dish.toLowerCase().indexOf(q) >= 0 && found.indexOf(dish) < 0) found.push(dish);
      });
    });
    return found;
  }

  // 검색어가 들어간 메뉴가 나오는 날을 진하게 칠하고, 옆에 목록으로 보여 준다
  function applyYearSearch() {
    var q = $('yearSearch').value.trim();
    var hits = [];
    yearCells.forEach(function (c) {
      var hit = q.length > 0 && c.meals.length > 0 && matchingDishes(c.meals, q).length > 0;
      if (hit) hits.push(c);
      c.box.classList.toggle('hit', hit);
    });

    var results = $('yearResults');
    clear(results);
    if (!q) return;

    results.appendChild(el('div', 'head', '‘' + q + '’ 나오는 날 ' + hits.length + '일'));
    hits.forEach(function (c) {
      var row = el('button', 'result');
      row.appendChild(el('div', 'name', (c.date.getMonth() + 1) + '월 ' + c.date.getDate() + '일 (' + DOW[c.date.getDay()] + ')'));
      row.appendChild(el('div', 'sub', matchingDishes(c.meals, q).join(', ')));
      row.onclick = function () { showDay(c.date); };
      results.appendChild(row);
    });
  }

  function onYearSearchInput() {
    if (mode !== 'year') return;
    // 검색을 시작하면 옆 칸을 결과 목록에 내주고, 지우면 다시 오늘 급식을 띄운다
    if ($('yearSearch').value.trim().length > 0) yearHover = null;
    else if (today.getFullYear() === yearCursor && store.get(today).length > 0) yearHover = today;
    renderYearDetail();
    applyYearSearch();
  }

  // ───────────────────────── 학교 검색 (첫 화면, 설정 탭 공용) ─────────────────────────

  function makeSchoolSearch(root, onPicked) {
    root.classList.add('search');
    var row = el('div', 'row');
    var query = el('input', 'lineBox');
    query.type = 'text';
    query.placeholder = '학교 이름';
    query.autocomplete = 'off';
    query.enterKeyHint = 'search';
    var go = el('button', 'go link', '검색');
    row.appendChild(query);
    row.appendChild(go);
    var status = el('div', 'status');
    status.hidden = true;
    var results = el('div', 'results');
    root.appendChild(row);
    root.appendChild(status);
    root.appendChild(results);

    var timer = 0, seq = 0;

    function say(text) {
      status.textContent = text;
      status.hidden = false;
    }

    async function search() {
      clearTimeout(timer);
      var q = query.value.trim();
      var mine = ++seq;

      if (q.length < 2) {
        clear(results);
        if (q.length === 0) status.hidden = true;
        else say('두 글자 이상 입력해 주세요.');
        return;
      }

      say('찾는 중…');
      var schools;
      try {
        schools = await searchSchools(q);
      } catch (ex) {
        if (mine === seq) {
          clear(results);
          say(describe(ex));
        }
        return;
      }
      if (mine !== seq) return;   // 그 사이 다른 검색이 시작됨

      clear(results);
      if (schools.length === 0) {
        say('‘' + q + '’ 검색 결과가 없습니다.');
        return;
      }
      status.hidden = true;
      schools.forEach(function (s) {
        var item = el('button', 'result');
        item.appendChild(el('div', 'name', s.name));
        item.appendChild(el('div', 'sub', s.address || s.officeName));
        item.onclick = function () { onPicked(s); };
        results.appendChild(item);
      });
      results.scrollTop = 0;
    }

    // 글자를 칠 때마다 요청하지 않고, 잠깐 멈추면 검색한다
    query.addEventListener('input', function () {
      clearTimeout(timer);
      timer = setTimeout(search, 450);
    });
    query.addEventListener('keydown', function (e) {
      if (e.key === 'Enter' && !e.isComposing) search();
    });
    go.onclick = search;

    return {
      input: query,
      focus: function () { query.focus(); },
      reset: function () {
        clearTimeout(timer);
        seq++;
        query.value = '';
        clear(results);
        status.hidden = true;
      },
      searchNow: function (q) {
        query.value = q;
        return search();
      }
    };
  }

  // ───────────────────────── 메뉴 / 설정 탭 ─────────────────────────

  function togglePanel(which) {
    if (openPanel === which) {
      closePanel();
      return;
    }
    openPanel = which;
    var panel = $('panel');
    $('menuContent').hidden = which !== 'menu';
    $('settingsContent').hidden = which !== 'settings';
    $('menuBtn').classList.toggle('on', which === 'menu');
    $('settingsBtn').classList.toggle('on', which === 'settings');
    // 탭은 누른 버튼 바로 아래를 기준점으로 커지고 줄어든다
    panel.classList.toggle('menu', which === 'menu');
    panel.classList.toggle('settings', which === 'settings');
    panel.hidden = false;
    void panel.offsetWidth;
    panel.classList.add('open');
  }

  function closePanel() {
    if (!openPanel) return;
    openPanel = null;
    $('menuBtn').classList.remove('on');
    $('settingsBtn').classList.remove('on');
    $('panel').classList.remove('open');
    setTimeout(function () {
      if (openPanel) return;   // 닫히는 도중에 다시 열렸다
      $('panel').hidden = true;
      hideSchoolSearch();
    }, 150);
  }

  function hideSchoolSearch() {
    panelSearch.reset();
    $('panelSearch').hidden = true;
    $('changeSchoolBtn').textContent = '학교 변경';
  }

  function onSchoolPicked(school) {
    setSchool(school);
    saveSettings();
    store.setSchool(school);
    showSchool();
    setupSearch.reset();
    hideSchoolSearch();
    closePanel();
    showDay(today);
  }

  // ───────────────────────── 새로고침, 키, 날짜 ─────────────────────────

  function refresh() {
    if (!hasSchool()) return;
    if (!fixedToday) today = todayDate();
    store.clear();
    reload();
  }

  function reload() {
    if (mode === 'month') return showMonth(monthCursor);
    if (mode === 'year') return showYear(yearCursor);
    return showDay(selected);
  }

  // 켜 둔 채로 자정이 지났으면 오늘을 다시 맞춘다
  function catchUpDate() {
    if (fixedToday || !hasSchool() || same(todayDate(), today)) return;
    var wasOnToday = same(selected, today);
    today = todayDate();
    if (wasOnToday) selected = today;
    reload();
  }

  // 안드로이드 뒤로 버튼: 탭을 닫거나 오늘 급식으로 돌아간다. 더 할 일이 없으면 false (앱이 닫힌다).
  window.todayMealBack = function () {
    if (openPanel) {
      closePanel();
      return true;
    }
    if (mode === 'month' || mode === 'year' || (mode === 'day' && !same(selected, today))) {
      showDay(today);
      return true;
    }
    return false;
  };

  // ───────────────────────── 시작 ─────────────────────────

  var setupSearch = makeSchoolSearch($('setupSearch'), onSchoolPicked);
  var panelSearch = makeSchoolSearch($('panelSearch'), onSchoolPicked);

  function gearPath() {
    // 톱니바퀴: 톱니 8개 + 가운데 구멍 (24x24 좌표계)
    var teeth = 8, c = 12, tip = 10.3, root = 7.7, hole = 3.2;
    var step = 2 * Math.PI / teeth, halfTip = step * 0.17, halfRoot = step * 0.29;
    function p(r, a) { return (c + r * Math.cos(a)).toFixed(3) + ',' + (c + r * Math.sin(a)).toFixed(3); }
    var d = '';
    for (var i = 0; i < teeth; i++) {
      var a = i * step - Math.PI / 2;
      d += (i === 0 ? 'M' : 'A' + root + ',' + root + ' 0 0 1 ') + p(root, a - halfRoot)
        + ' L' + p(tip, a - halfTip)
        + ' A' + tip + ',' + tip + ' 0 0 1 ' + p(tip, a + halfTip)
        + ' L' + p(root, a + halfRoot) + ' ';
    }
    d += 'A' + root + ',' + root + ' 0 0 1 ' + p(root, -Math.PI / 2 - halfRoot) + ' Z ';
    d += 'M' + (c + hole) + ',' + c + ' A' + hole + ',' + hole + ' 0 1 1 ' + (c - hole) + ',' + c
      + ' A' + hole + ',' + hole + ' 0 1 1 ' + (c + hole) + ',' + c + ' Z';
    return d;
  }

  function wire() {
    $('gearPath').setAttribute('d', gearPath());

    $('refreshBtn').onclick = refresh;
    $('menuBtn').onclick = function () { togglePanel('menu'); };
    $('settingsBtn').onclick = function () { togglePanel('settings'); };

    $('navToday').onclick = function () { closePanel(); showDay(today); };
    $('navMonth').onclick = function () { closePanel(); showMonth(today); };
    $('navYear').onclick = function () { closePanel(); showYear(today.getFullYear()); };

    $('monthPrev').onclick = function () { showMonth(addMonths(monthCursor, -1)); };
    $('monthNext').onclick = function () { showMonth(addMonths(monthCursor, 1)); };
    $('yearPrev').onclick = function () { showYear(yearCursor - 1); };
    $('yearNext').onclick = function () { showYear(yearCursor + 1); };
    $('yearSearch').addEventListener('input', onYearSearchInput);

    $('changeSchoolBtn').onclick = function () {
      if (!$('panelSearch').hidden) {
        hideSchoolSearch();
        return;
      }
      $('panelSearch').hidden = false;
      $('changeSchoolBtn').textContent = '취소';
      panelSearch.focus();
    };

    function pickTheme(theme) {
      settings.theme = theme;
      saveSettings();
      applyTheme();
    }
    $('themeLight').onclick = function () { pickTheme('light'); };
    $('themeDark').onclick = function () { pickTheme('dark'); };

    // 탭 바깥을 누르면 닫힌다. 마우스가 탭 밖으로 나가도 닫힌다 (학교를 검색하는 중에는 그대로 둔다).
    document.addEventListener('pointerdown', function (e) {
      lastPointer = e.pointerType || 'mouse';
      if (!openPanel) return;
      if ($('panel').contains(e.target) || $('menuBtn').contains(e.target) || $('settingsBtn').contains(e.target)) return;
      closePanel();
    }, true);
    document.addEventListener('pointermove', function (e) { lastPointer = e.pointerType || 'mouse'; }, true);
    $('panel').addEventListener('pointerleave', function (e) {
      if (e.pointerType !== 'mouse') return;
      if (document.activeElement === panelSearch.input) return;
      closePanel();
    });

    document.addEventListener('keydown', function (e) {
      var typing = /^(INPUT|TEXTAREA)$/.test((document.activeElement || {}).tagName || '');
      if (e.key === 'Escape' && openPanel) {
        closePanel();
        e.preventDefault();
      } else if ((e.key === 'ArrowLeft' || e.key === 'ArrowRight') && mode === 'day' && !openPanel && !typing) {
        stepDay(e.key === 'ArrowLeft' ? -1 : 1);
        e.preventDefault();
      }
    });

    // 하루 화면에서 좌우로 밀면 앞뒤 급식일
    var startX = 0, startY = 0, tracking = false;
    $('dayView').addEventListener('touchstart', function (e) {
      tracking = e.touches.length === 1;
      if (tracking) {
        startX = e.touches[0].clientX;
        startY = e.touches[0].clientY;
      }
    }, { passive: true });
    $('dayView').addEventListener('touchend', function (e) {
      if (!tracking || openPanel) return;
      tracking = false;
      var dx = e.changedTouches[0].clientX - startX, dy = e.changedTouches[0].clientY - startY;
      if (Math.abs(dx) > 60 && Math.abs(dx) > Math.abs(dy) * 1.8) stepDay(dx < 0 ? 1 : -1);
    }, { passive: true });

    window.addEventListener('resize', fitHero);
    if (typeof ResizeObserver !== 'undefined') new ResizeObserver(fitHero).observe($('dishBox'));
    if (document.fonts && document.fonts.ready) document.fonts.ready.then(fitHero);

    window.addEventListener('focus', catchUpDate);
    document.addEventListener('visibilitychange', function () { if (!document.hidden) catchUpDate(); });
  }

  // 화면 확인용 주소 옵션 (저장된 학교는 건드리지 않는다):
  //   ?school=B10|7010083|서울고등학교&view=month&theme=dark&panel=menu&date=20261112&query=치킨&search=서울고
  function readDevOptions() {
    var p = new URLSearchParams(location.search);
    var o = { view: p.get('view'), panel: p.get('panel'), query: p.get('query'), search: p.get('search') };
    var school = (p.get('school') || '').split('|');
    if (school.length >= 3) {
      settings.volatile = true;
      settings.officeCode = school[0];
      settings.schoolCode = school[1];
      settings.schoolName = school[2];
      settings.officeName = '';
      settings.schoolAddress = '';
    }
    if (p.get('theme')) {
      settings.volatile = true;
      settings.theme = p.get('theme') === 'dark' ? 'dark' : 'light';
    }
    var date = parseYmd(p.get('date'));
    if (date) {
      fixedToday = true;
      today = selected = date;
      monthCursor = firstOfMonth(date);
      yearCursor = date.getFullYear();
    }
    return o;
  }

  async function start() {
    loadSettings();
    var dev = readDevOptions();
    wire();
    applyTheme();
    showSchool();

    if (!hasSchool()) {
      setMode('setup');
      if (dev.search) setupSearch.searchNow(dev.search);
      else if (lastPointer === 'mouse' && window.matchMedia('(hover: hover)').matches) setupSearch.focus();
    } else {
      store.setSchool(toSchool());
      if (dev.view === 'month') await showMonth(today);
      else if (dev.view === 'year') {
        if (dev.query) $('yearSearch').value = dev.query;
        await showYear(today.getFullYear());
        onYearSearchInput();
      } else await showDay(today);
    }

    if (dev.panel === 'menu' || dev.panel === 'settings') {
      togglePanel(dev.panel);
      if (dev.search && dev.panel === 'settings') {
        $('changeSchoolBtn').click();
        panelSearch.searchNow(dev.search);
      }
    }
  }

  start();
})();
