/* =====================================================================
   wwwroot/js/app-standard.js        Admin 360 - the common code of ALL pages

   Every page uses these helpers, so every page works the same way:

     SD.toast(msg, isError)                small message at the bottom
     SD.post(url, {id: 5})                 POST + anti-forgery token, returns the JSON
     SD.confirm({...})                     the same Yes / No box everywhere (can ask a reason)
     SD.lock(button, 'Saving...')          disable a button + spinner (entry pages)
     SD.initList()                         LIST / REPORT page: tabs, tiles, search, date, sort
     SD.exportExcel(table, 'file name')    REPORT page: download the table as Excel
     SD.print(table, 'Title')              REPORT page: print the table

   Also in this file:  sidebar memory (no "reload" feeling) and  smart navigation (optional).

   SECTIONS:  [1] settings   [2] small helpers   [3] toast / post / confirm / lock
              [4] list page  [5] report export   [6] sidebar memory   [7] smart navigation
   ===================================================================== */
(function () {
    'use strict';

    /* [1] ===== SETTINGS ===== */
    var CFG = {
        SMART_NAV: true,         // true  = sidebar links open the page WITHOUT reloading the left menu / top bar (only pages marked data-smart-page)
        TOAST_MS: 3200,          // how long the message at the bottom stays
        REMEMBER_SIDEBAR: true   // keep the scroll position of the left menu between pages
    };

    /* [2] ===== SMALL HELPERS ===== */
    var $ = function (id) { return document.getElementById(id); };
    function esc(s) { return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]; }); }
    function remember(key, val) {                       // small memory in this browser (never breaks the page)
        try { if (val === undefined) return sessionStorage.getItem(key); sessionStorage.setItem(key, val); } catch (e) { }
        return null;
    }
    var SD = window.SD = { config: CFG, esc: esc };

    /* [3] ===== TOAST / POST / CONFIRM / LOCK ===== */
    var toastTimer;
    SD.toast = function (msg, isError) {
        var t = $('sdToast');
        if (!t) { t = document.createElement('div'); t.id = 'sdToast'; document.body.appendChild(t); }
        t.textContent = msg;
        t.className = 'sd-toast show' + (isError ? ' err' : '');
        clearTimeout(toastTimer);
        toastTimer = setTimeout(function () { t.className = 'sd-toast'; }, CFG.TOAST_MS);
    };

    // POST form data (+ anti-forgery token) and read the JSON answer
    SD.post = function (url, data) {
        var body = new URLSearchParams();
        Object.keys(data || {}).forEach(function (k) { body.append(k, data[k]); });
        var tk = document.querySelector('input[name="__RequestVerificationToken"]');
        if (tk) body.append('__RequestVerificationToken', tk.value);
        return fetch(url, {
            method: 'POST', credentials: 'same-origin',
            headers: { 'Content-Type': 'application/x-www-form-urlencoded' }, body: body.toString()
        }).then(function (r) { if (!r.ok) throw new Error(r.status); return r.json(); });
    };

    // Yes / No box.  options:
    //   title, text (plain text), tone ('success' green | 'danger' red), icon ('fa-check'), yesText, noText,
    //   reason: { label: 'Reason (optional)', max: 200 }      -> shows a text box
    //   run: function (reason) { return SD.post(...) }        -> called on Yes; box shows a spinner and
    //                                                            closes when the answer has success = true
    // returns a Promise: the answer of run() on success, or null when the user pressed No / Esc
    SD.confirm = function (o) {
        o = o || {};
        var ov = $('sdOverlay');
        if (!ov) {
            ov = document.createElement('div'); ov.id = 'sdOverlay'; ov.className = 'sd-overlay';
            ov.innerHTML = '<div class="sd-box"><div class="big" id="sdIcon"></div><h5 id="sdHead"></h5><p id="sdText"></p>' +
                '<div id="sdReasonWrap" style="display:none"><label class="form-label small fw-semibold d-block text-start" id="sdReasonLbl"></label>' +
                '<textarea id="sdReason" class="form-control" rows="3"></textarea></div><div class="sd-err" id="sdErr" style="display:none"></div>' +
                '<div class="d-flex justify-content-center gap-2"><button type="button" class="btn btn-light" id="sdNo"></button>' +
                '<button type="button" class="btn" id="sdYes"></button></div></div>';
            document.body.appendChild(ov);
        }
        var good = o.tone !== 'danger';
        $('sdIcon').innerHTML = '<i class="fas ' + (o.icon || (good ? 'fa-check' : 'fa-xmark')) + '"></i>';
        $('sdIcon').style.cssText = good ? 'background:#dcfce7;color:#15803d' : 'background:#fee2e2;color:#b91c1c';
        $('sdHead').textContent = o.title || 'Are you sure?';
        $('sdText').innerHTML = o.text ? '<b>' + esc(o.text) + '</b>' : '';
        $('sdText').style.display = o.text ? '' : 'none';
        $('sdReasonWrap').style.display = o.reason ? '' : 'none';
        if (o.reason) {
            $('sdReasonLbl').textContent = o.reason.label || 'Reason (optional)';
            $('sdReason').maxLength = o.reason.max || 200; $('sdReason').value = '';
        }
        $('sdErr').style.display = 'none';
        $('sdNo').textContent = o.noText || 'Cancel';
        var yes = $('sdYes');
        yes.className = 'btn ' + (good ? 'btn-success' : 'btn-danger');
        yes.innerHTML = '<i class="fas ' + (o.icon || (good ? 'fa-check' : 'fa-xmark')) + ' me-1"></i> ' + esc(o.yesText || 'Yes');
        yes.disabled = false;
        ov.classList.add('show');

        return new Promise(function (resolve) {
            function close(result) {
                ov.classList.remove('show');
                yes.onclick = $('sdNo').onclick = ov.onclick = null; document.removeEventListener('keydown', onKey);
                resolve(result);
            }
            function onKey(e) { if (e.key === 'Escape') close(null); }
            document.addEventListener('keydown', onKey);
            $('sdNo').onclick = function () { close(null); };
            ov.onclick = function (e) { if (e.target === ov) close(null); };
            yes.onclick = function () {
                var reason = o.reason ? $('sdReason').value.trim() : '';
                if (!o.run) { close({ success: true, reason: reason }); return; }
                var label = yes.innerHTML;
                yes.disabled = true; yes.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span> Please wait...';
                Promise.resolve(o.run(reason)).then(function (res) {
                    if (res && res.success === false) throw new Error(res.message || 'Action failed.');
                    close(res || { success: true });
                }).catch(function (err) {
                    $('sdErr').textContent = (err && err.message && isNaN(err.message)) ? err.message : 'Server error. Please try again.';
                    $('sdErr').style.display = ''; yes.disabled = false; yes.innerHTML = label;
                });
            };
        });
    };

    // button spinner for Save buttons:  SD.lock(btn, 'Saving...')
    SD.lock = function (btn, text) {
        if (!btn) return;
        btn.disabled = true;
        btn.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span> ' + esc(text || 'Please wait...');
    };

    /* [4] ===== LIST / REPORT PAGE =====
       Needs this markup (see Views/_Templates/ListPage.cshtml):
         .sd-tab[data-pane]  -> .sd-pane#pane-xxx      tabs (optional)
         .sd-tile[data-day]  with [data-num]           All / Today / Upcoming / Past tiles (optional)
         #sdSearch  #sdDate  #sdClear  #sdResetSort    search box, date box, clear link, default-order link
         every <tr> : data-search, data-day, data-date, data-i, data-k-xxx (value used to sort column "xxx")
         every sortable <th class="sortable" data-sort="xxx">
         paging (optional): <div class="sd-pane" data-page-size="25"> -> pager under the table (10 / 25 / 50 / 100 / All rows).
           Rows of other pages only get the class "sd-pg-off", so Print / Excel still take every matching row.   */
    var PAGE_SIZES = [10, 25, 50, 100, 0];                 // 0 = all rows
    SD.initList = function () {
        var tabs = document.querySelectorAll('.sd-tab'), panes = document.querySelectorAll('.sd-pane'),
            tiles = document.querySelectorAll('.sd-tile');
        if (!panes.length) return;
        var dayKey = 'sdDay:' + location.pathname, dayFilter = remember(dayKey) || 'all', sortKey = null, sortDir = 1;
        var search = $('sdSearch'), dateBox = $('sdDate'), clear = $('sdClear'), resetSort = $('sdResetSort');
        var sizeKey = 'sdPageSize:' + location.pathname, pageOf = {};

        function pageSize(pane) {
            var saved = remember(sizeKey);
            var n = saved !== null && saved !== '' ? Number(saved) : Number(pane.getAttribute('data-page-size'));
            return PAGE_SIZES.indexOf(n) >= 0 ? n : 25;
        }
        // pager under the table: "Showing 26-50 of 312"  rows per page  << < 1 2 3 ... > >>
        function drawPager(pane, total, page, pages, size) {
            var pager = pane.querySelector('.sd-pager');
            if (!pager) {
                pager = document.createElement('div'); pager.className = 'sd-pager';
                var wrap = pane.querySelector('.sd-wrap');
                if (wrap) wrap.after(pager); else pane.appendChild(pager);
                pager.addEventListener('click', function (e) {
                    var b = e.target.closest('button[data-pg]'); if (!b || b.disabled) return;
                    pageOf[pane.id] = Number(b.dataset.pg); refresh(true);
                    var top = pane.getBoundingClientRect().top; if (top < 0) window.scrollBy(0, top - 70);
                });
                pager.addEventListener('change', function (e) {
                    if (!e.target.matches('select.sd-pg-size')) return;
                    remember(sizeKey, e.target.value); pageOf[pane.id] = 1; refresh(true);
                });
            }
            pager.style.display = total ? '' : 'none';
            if (!total) return;
            var from = size ? (page - 1) * size + 1 : 1, to = size ? Math.min(total, page * size) : total;
            var nums = [], add = function (n) { if (nums.indexOf(n) < 0 && n >= 1 && n <= pages) nums.push(n); };
            add(1); for (var n = page - 2; n <= page + 2; n++) add(n); add(pages);
            nums.sort(function (a, b) { return a - b; });
            var btn = function (n, html, title, on) {
                return '<button type="button" data-pg="' + n + '"' + (title ? ' title="' + title + '"' : '') +
                    (on ? ' class="on" aria-current="page"' : '') + ((n < 1 || n > pages || (on && html === String(n))) ? ' disabled' : '') + '>' + html + '</button>';
            };
            var links = btn(1, '&laquo;', 'First page') + btn(page - 1, '&lsaquo;', 'Previous page');
            nums.forEach(function (n, i) {
                if (i && n - nums[i - 1] > 1) links += '<span class="gap">&hellip;</span>';
                links += btn(n, String(n), '', n === page);
            });
            links += btn(page + 1, '&rsaquo;', 'Next page') + btn(pages, '&raquo;', 'Last page');
            pager.innerHTML =
                '<span class="sd-pg-info">Showing <b>' + from + '-' + to + '</b> of <b>' + total + '</b></span>' +
                '<label class="sd-pg-rows">Rows <select class="sd-pg-size" data-native>' +
                PAGE_SIZES.map(function (s) { return '<option value="' + s + '"' + (s === size ? ' selected' : '') + '>' + (s || 'All') + '</option>'; }).join('') +
                '</select></label>' +
                (pages > 1 ? '<nav class="sd-pg-nav" aria-label="Pages">' + links + '</nav>' : '');
        }

        function activePane() { return document.querySelector('.sd-pane.active') || panes[0]; }

        function openPane(id) {
            tabs.forEach(function (t) { t.classList.toggle('active', t.dataset.pane === id); });
            panes.forEach(function (p) { p.classList.toggle('active', p.id === 'pane-' + id); });
            try { history.replaceState(history.state, '', '#' + id); } catch (e) { }
            refresh();
        }
        tabs.forEach(function (t) { t.addEventListener('click', function () { openPane(t.dataset.pane); }); });

        tiles.forEach(function (t) {
            t.addEventListener('click', function () { dayFilter = t.dataset.day; remember(dayKey, dayFilter); refresh(); });
        });

        function refresh(keepPage) {
            var pane = activePane(), q = search ? search.value.trim().toLowerCase() : '', d = dateBox ? dateBox.value : '';
            var rows = Array.prototype.slice.call(pane.querySelectorAll('tbody tr:not(.sd-total)'));      // a Total row is never filtered / sorted

            var c = { all: rows.length, today: 0, future: 0, past: 0 };            // numbers on the tiles
            rows.forEach(function (tr) { if (c[tr.dataset.day] !== undefined) c[tr.dataset.day]++; });
            Object.keys(c).forEach(function (k) { var n = document.querySelector('[data-num="' + k + '"]'); if (n) n.textContent = c[k]; });
            tiles.forEach(function (t) { t.classList.toggle('active', t.dataset.day === dayFilter); });

            var shown = 0, matched = [];
            rows.forEach(function (tr) {
                var ok = (dayFilter === 'all' || tr.dataset.day === dayFilter) &&
                    (!d || tr.dataset.date === d) &&
                    (!q || (tr.dataset.search || '').indexOf(q) !== -1);
                tr.style.display = ok ? '' : 'none';
                tr.classList.remove('sd-pg-off');
                if (ok) { shown++; matched.push(tr); }
            });
            if (pane.hasAttribute('data-page-size')) {
                var size = pageSize(pane), pages = size ? Math.max(1, Math.ceil(shown / size)) : 1;
                var page = keepPage === true ? Math.min(Math.max(1, pageOf[pane.id] || 1), pages) : 1;
                pageOf[pane.id] = page;
                if (size) matched.forEach(function (tr, i) { if (i < (page - 1) * size || i >= page * size) tr.classList.add('sd-pg-off'); });
                drawPager(pane, shown, page, pages, size);
            }
            var wrap = pane.querySelector('.sd-wrap'), none = pane.querySelector('[data-empty="filter"]');
            if (wrap) wrap.style.display = shown ? '' : 'none';
            if (none) none.style.display = (rows.length && !shown) ? '' : 'none';
            if (clear) clear.style.display = (q || d || dayFilter !== 'all') ? '' : 'none';
            if (resetSort) resetSort.style.display = sortKey ? '' : 'none';
        }
        if (search) search.addEventListener('input', refresh);
        if (dateBox) dateBox.addEventListener('change', refresh);
        if (clear) clear.addEventListener('click', function () {
            if (search) search.value = ''; if (dateBox) dateBox.value = '';
            dayFilter = 'all'; remember(dayKey, dayFilter); refresh();
        });

        function sortRows() {
            panes.forEach(function (pane) {
                var body = pane.querySelector('tbody'); if (!body) return;
                var rows = Array.prototype.slice.call(body.querySelectorAll('tr:not(.sd-total)'));
                rows.sort(function (a, b) {
                    if (!sortKey) return Number(a.dataset.i) - Number(b.dataset.i);          // default order
                    var x = a.getAttribute('data-k-' + sortKey) || '', y = b.getAttribute('data-k-' + sortKey) || '';
                    var r = (x !== '' && y !== '' && !isNaN(x) && !isNaN(y)) ? (Number(x) - Number(y)) : x.localeCompare(y);
                    return r * sortDir;
                });
                rows.forEach(function (tr) { body.appendChild(tr); });
                body.querySelectorAll('tr.sd-total').forEach(function (tr) { body.appendChild(tr); });   // Total stays at the bottom
            });
            document.querySelectorAll('.sd-table th.sortable').forEach(function (th) {
                var on = th.dataset.sort === sortKey;
                th.classList.remove('asc', 'desc'); if (on) th.classList.add(sortDir === 1 ? 'asc' : 'desc');
                var arr = th.querySelector('.arr'); if (arr) arr.className = 'fas arr ' + (on ? (sortDir === 1 ? 'fa-sort-up' : 'fa-sort-down') : 'fa-sort');
            });
            refresh();
        }
        document.querySelectorAll('.sd-table th.sortable').forEach(function (th) {
            th.addEventListener('click', function () {
                if (sortKey === th.dataset.sort) sortDir = -sortDir; else { sortKey = th.dataset.sort; sortDir = 1; }
                sortRows();
            });
        });
        if (resetSort) resetSort.addEventListener('click', function () { sortKey = null; sortDir = 1; sortRows(); });

        var h = (location.hash || '').replace('#', '');
        if (h && $('pane-' + h)) openPane(h); else refresh();
    };

    /* [5] ===== REPORT EXPORT ===== */
    function visibleTableCopy(table) {          // copy of the table without hidden rows / action column
        var t = table.cloneNode(true);
        Array.prototype.forEach.call(t.querySelectorAll('tr'), function (tr, i) {
            var orig = table.querySelectorAll('tr')[i];
            if (orig && orig.style.display === 'none') tr.remove();
        });
        t.querySelectorAll('.no-export, [data-label="Action"], th.sd-noexport').forEach(function (e) { e.remove(); });
        return t;
    }
    SD.exportExcel = function (tableOrSelector, fileName) {
        var table = typeof tableOrSelector === 'string' ? document.querySelector(tableOrSelector) : tableOrSelector;
        if (!table) return;
        if (!window.XLSX) { SD.toast('Excel library is not loaded on this page.', true); return; }
        var wb = XLSX.utils.table_to_book(visibleTableCopy(table), { sheet: 'Report' });
        XLSX.writeFile(wb, (fileName || 'Report') + '.xlsx');
    };
    SD.print = function (tableOrSelector, title) {
        var table = typeof tableOrSelector === 'string' ? document.querySelector(tableOrSelector) : tableOrSelector;
        if (!table) return;
        var w = window.open('', '_blank');
        w.document.write('<html><head><title>' + esc(title || 'Report') + '</title><style>body{font-family:Arial;font-size:12px}h3{margin:0 0 10px}' +
            'table{border-collapse:collapse;width:100%}th,td{border:1px solid #999;padding:5px 8px;text-align:left}th{background:#eee}</style></head><body>' +
            '<h3>' + esc(title || 'Report') + '</h3>' + visibleTableCopy(table).outerHTML + '</body></html>');
        w.document.close(); w.focus(); w.print();
    };

    /* [6] ===== SIDEBAR MEMORY =====
       The left menu is drawn again by the server on every page. To make it feel like it never reloads:
       - the scroll position of the menu is saved before leaving and restored at once on the next page
       - the active link is scrolled into view when nothing is saved                                  */
    function sidebarScroller() {
        return document.querySelector('.sidebar-wrapper [data-overlayscrollbars-viewport]') || document.querySelector('.sidebar-wrapper');
    }
    function restoreSidebar() {
        if (!CFG.REMEMBER_SIDEBAR) return;
        var el = sidebarScroller(); if (!el) return;
        var saved = remember('sdSideScroll');
        if (saved !== null) { el.scrollTop = Number(saved) || 0; return; }
        var act = document.querySelector('.app-sidebar .nav-link.active');
        if (act && act.scrollIntoView) act.scrollIntoView({ block: 'nearest' });
    }
    function saveSidebar() { var el = sidebarScroller(); if (el) remember('sdSideScroll', String(el.scrollTop)); }
    restoreSidebar();                                                   // the sidebar is above this script, so it exists already
    document.addEventListener('DOMContentLoaded', restoreSidebar);
    window.addEventListener('load', function () {                       // AdminLTE builds its scrollbar later - restore once more
        restoreSidebar();
        setTimeout(restoreSidebar, 150);
        document.body.classList.remove('sd-preload');                   // animations are allowed again
    });
    window.addEventListener('pagehide', saveSidebar);
    document.addEventListener('click', function (e) { if (e.target.closest('.app-sidebar a[href]')) saveSidebar(); }, true);

    /* [7] ===== SMART NAVIGATION (optional, SD.config.SMART_NAV) =====
       A sidebar link loads only the page content (.app-content) with fetch() and swaps it in.
       The top bar, the left menu and the scripts of the layout stay as they are - nothing reloads.
       Anything unusual (login page, file download, error) falls back to the normal full page load. */
    var nav = { bar: null, busy: 0 };
    function bar(state) {
        if (!nav.bar) { nav.bar = document.createElement('div'); nav.bar.id = 'sdNavBar'; document.body.appendChild(nav.bar); }
        nav.bar.className = state || '';
    }
    function runScripts(list) {                                          // list = [{src, text}] in page order
        var queue = [], origDoc = document.addEventListener, origWin = window.addEventListener;
        // pages written as  document.addEventListener('DOMContentLoaded', ...)  would never fire now -> call them ourselves
        document.addEventListener = function (t, f, o) { if (t === 'DOMContentLoaded') { queue.push(f); return; } return origDoc.call(document, t, f, o); };
        window.addEventListener = function (t, f, o) { if (t === 'load') { queue.push(f); return; } return origWin.call(window, t, f, o); };
        function finish() {
            document.addEventListener = origDoc; window.addEventListener = origWin;
            queue.forEach(function (f) { try { f.call(document, new Event('DOMContentLoaded')); } catch (e) { console.error(e); } });
        }
        return list.reduce(function (p, s) {
            return p.then(function () {
                if (s.src) {
                    if (document.querySelector('script[src="' + s.src + '"]')) return;     // library already loaded by the layout
                    return new Promise(function (ok) {
                        var el = document.createElement('script'); el.src = s.src; el.onload = el.onerror = ok; document.head.appendChild(el);
                    });
                }
                try { (0, eval)(s.text); } catch (e) { console.error('Page script error:', e); }   // indirect eval: let / const can be declared again
            });
        }, Promise.resolve()).then(finish, finish);
    }
    function collectScripts(root) {
        return Array.prototype.map.call(root.querySelectorAll('script'), function (s) {
            var src = s.getAttribute('src');
            return { src: src || '', text: s.textContent };
        });
    }
    function smartLoad(url, push) {
        bar('run');
        fetch(url, { credentials: 'same-origin', headers: { 'X-Requested-With': 'SmartNav' } }).then(function (r) {
            var type = r.headers.get('content-type') || '';
            if (!r.ok || type.indexOf('text/html') < 0 || /\/(login|accessdenied)/i.test(r.url)) throw 'full';
            return r.text().then(function (t) { return { text: t, url: r.url }; });
        }).then(function (res) {
            var doc = new DOMParser().parseFromString(res.text, 'text/html');
            var inNew = doc.getElementById('appContent'), inOld = $('appContent');
            if (!inNew || !inOld) throw 'full';
            if (!inNew.querySelector('[data-smart-page]')) throw 'full';          // old style page -> normal full load (safe)

            //var newNav = doc.getElementById('navigation'), oldNav = $('navigation');
            //if (newNav && oldNav && newNav.innerHTML !== oldNav.innerHTML) oldNav.innerHTML = newNav.innerHTML;   // other module / other active link
            // only the menu INSIDE the left sidebar (the top bar may also use id="navigation")
            var newNav = doc.querySelector('.app-sidebar .sidebar-menu'), oldNav = document.querySelector('.app-sidebar .sidebar-menu');
            if (newNav && oldNav && newNav.innerHTML !== oldNav.innerHTML) oldNav.innerHTML = newNav.innerHTML;   // other module / other active link

            var scripts = collectScripts(inNew).concat(collectScripts(doc.getElementById('pageScripts') || document.createElement('div')));
            inNew.querySelectorAll('script').forEach(function (s) { s.remove(); });
            document.title = doc.title;
            inOld.innerHTML = inNew.innerHTML;
            if ($('pageScripts')) $('pageScripts').innerHTML = '';
            if (push) history.pushState({ sd: 1 }, '', res.url);
            window.scrollTo(0, 0);
            return runScripts(scripts);
        }).then(function () { bar('done'); setTimeout(function () { bar(''); }, 400); })
            .catch(function (e) { if (e === 'full') location.href = url; else { console.error(e); location.href = url; } });
    }
    if (CFG.SMART_NAV) {
        document.addEventListener('click', function (e) {
            var a = e.target.closest('a[href]');
            if (!a || e.defaultPrevented || e.button !== 0 || e.ctrlKey || e.metaKey || e.shiftKey || a.target || a.hasAttribute('download') || a.hasAttribute('data-no-smart')) return;
            if (!a.closest('.app-sidebar') && !a.hasAttribute('data-smart')) return;       // only sidebar links (or links marked data-smart)
            var href = a.getAttribute('href');
            if (!href || href === '#' || href.indexOf('javascript:') === 0) return;
            var u = new URL(a.href, location.href);
            if (u.origin !== location.origin) return;
            e.preventDefault();
            if (u.pathname === location.pathname && u.search === location.search) return;
            smartLoad(u.href, true);
        });
        window.addEventListener('popstate', function () { smartLoad(location.href, false); });
    }

    /* [8] ===== EVERY DROP DOWN = SEARCHABLE (Select2, like Create Meeting > Location), EVERY DATE BOX = DD/MM/YYYY =====
       Runs by itself on every page (also after smart navigation and for boxes added later). Nothing to do in the views.
       Opt out with the attribute  data-native  on a <select> / <input type="date">.
       DROP DOWN: the <select> stays the real field. Page code keeps working:
         sel.value = x / $(sel).val(x)          -> the Select2 box shows the new value
         addEventListener('change') / onchange  -> still called when the user picks a value (Select2 alone only calls jQuery handlers)
         drop downs that a page turns into Select2 itself (Meeting pages) are left alone.
       DATE: the <input type="date"> becomes a hidden field that keeps the value as yyyy-mm-dd (forms, filters and page code
         see no change); next to it a text box shows / takes DD/MM/YYYY and opens a small calendar.                    */
    var S2_ATTR = 'data-sd-s2';
    function jqS2() { return (window.jQuery && jQuery.fn && jQuery.fn.select2) ? jQuery : null; }
    function ownS2(sel) { var jq = jqS2(); return !!(jq && sel._sdS2 && jq(sel).data('select2') === sel._sdS2); }
    function refreshS2(sel) { if (ownS2(sel)) jqS2()(sel).trigger('change.select2'); }      // namespaced: only Select2 redraws

    var selValue = Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, 'value'),
        selIndex = Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, 'selectedIndex');

    var jqPatched = false;
    function patchJq() {
        var jq = jqS2();
        if (jqPatched || !jq) return;
        jqPatched = true;
        // $(sel).trigger('change') of Select2 / page code -> a real DOM event, so native listeners run too (jQuery ones once)
        ['change', 'input'].forEach(function (type) {
            var old = jq.event.special[type] || {}, prevTrigger = old.trigger;
            jq.event.special[type] = jq.extend({}, old, {
                trigger: function (e) {
                    if (this.nodeName === 'SELECT' && ownS2(this) && !(e && e.namespace)) {
                        this.dispatchEvent(new Event(type, { bubbles: true }));
                        return false;
                    }
                    return prevTrigger ? prevTrigger.apply(this, arguments) : undefined;
                }
            });
        });
        // $(sel).val(x) sets the options directly (not sel.value) -> redraw too
        var hook = jq.valHooks.select, origSet = hook && hook.set;
        if (origSet) hook.set = function (elem, value) { var r = origSet.apply(this, arguments); refreshS2(elem); return r; };
        // Select2 4.1 does not always focus the search box
        jq(document).on('select2:open', function (e) {
            if (!e.target || !e.target.hasAttribute(S2_ATTR)) return;
            setTimeout(function () { var f = document.querySelector('.select2-container--open .select2-search__field'); if (f) f.focus(); }, 0);
        });
    }

    function enhanceSelect(sel) {
        var jq = jqS2();
        if (!jq || sel.hasAttribute(S2_ATTR) || sel.hasAttribute('data-native') || sel.closest('[data-native]')) return;
        if (sel.classList.contains('select2-hidden-accessible') || sel.size > 1 || sel.hidden) return;   // list boxes stay as they are
        if (sel.style.display === 'none' || getComputedStyle(sel).display === 'none') return;       // a drop down the page hides on purpose
        if (sel.closest('.dataTables_length, .swal2-container, .select2-container')) return;
        patchJq();
        var modal = sel.closest('.modal'), $s = jq(sel);
        try {
            $s.select2({
                theme: 'default', width: '100%',
                dropdownParent: modal ? jq(modal) : jq(document.body),
                language: { noResults: function () { return 'No result found'; } }
            });
        } catch (err) { console.error('Select2 failed', err); return; }
        sel.setAttribute(S2_ATTR, '1');
        sel._sdS2 = $s.data('select2');
        var box = sel.nextElementSibling;
        if (box && box.classList.contains('select2-container')) {
            box.classList.add('sd-s2');
            if (sel.classList.contains('form-select-sm')) box.classList.add('sd-s2-sm');
            if (sel.style.maxWidth) box.style.maxWidth = sel.style.maxWidth;
        }
        if (sel._sdS2 && sel._sdS2.$dropdown) sel._sdS2.$dropdown.addClass('sd-s2-dd');
        Object.defineProperty(sel, 'value', { configurable: true, get: function () { return selValue.get.call(this); }, set: function (v) { selValue.set.call(this, v); refreshS2(this); } });
        Object.defineProperty(sel, 'selectedIndex', { configurable: true, get: function () { return selIndex.get.call(this); }, set: function (v) { selIndex.set.call(this, v); refreshS2(this); } });
    }

    // ---------- DD/MM/YYYY date box ----------
    var inValue = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value');
    var MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];
    function pad2(n) { return (n < 10 ? '0' : '') + n; }
    function isoOf(d) { return d.getFullYear() + '-' + pad2(d.getMonth() + 1) + '-' + pad2(d.getDate()); }
    function dateOfIso(v) { var m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(v || ''); return m ? new Date(+m[1], +m[2] - 1, +m[3]) : null; }
    function dmyOfIso(v) { var m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(v || ''); return m ? m[3] + '/' + m[2] + '/' + m[1] : ''; }
    function isoOfDmy(t) {                                     // "5/1/2026", "05-01-2026", "05.01.2026" -> "2026-01-05", wrong date -> null
        var m = /^(\d{1,2})[\/\-.](\d{1,2})[\/\-.](\d{4})$/.exec((t || '').trim());
        if (!m) return null;
        var d = +m[1], mo = +m[2], y = +m[3], dt = new Date(y, mo - 1, d);
        return (dt.getFullYear() === y && dt.getMonth() === mo - 1 && dt.getDate() === d) ? isoOf(dt) : null;
    }
    function inRange(inp, iso) {
        var min = inp.getAttribute('min'), max = inp.getAttribute('max');
        return (!min || iso >= min) && (!max || iso <= max);
    }
    function setDate(inp, iso) {                               // new value + the events a real date box would fire
        var old = inValue.get.call(inp);
        inp.value = iso;
        if (old !== iso) {
            inp.dispatchEvent(new Event('input', { bubbles: true }));
            inp.dispatchEvent(new Event('change', { bubbles: true }));
        }
    }

    function enhanceDate(inp) {
        if (inp.hasAttribute('data-sd-dmy') || inp.hasAttribute('data-native') || inp.closest('[data-native]')) return;
        var box = document.createElement('input');
        box.type = 'text';
        box.className = (inp.className + ' sd-dmy').trim();
        box.placeholder = 'DD/MM/YYYY';
        box.autocomplete = 'off';
        box.inputMode = 'numeric';
        box.maxLength = 10;
        if (inp.title) box.title = inp.title;
        if (inp.style.cssText) box.style.cssText = inp.style.cssText;
        box.disabled = inp.disabled; box.readOnly = inp.readOnly; box.required = inp.required;
        var lbl = inp.labels && inp.labels[0];                  // read before the type changes (hidden inputs have no label)
        box.setAttribute('aria-label', inp.getAttribute('aria-label') || (lbl ? lbl.textContent.trim() : 'Date') + ' (DD/MM/YYYY)');
        if (lbl) lbl.addEventListener('click', function (e) { e.preventDefault(); box.focus(); });

        inp.setAttribute('data-sd-dmy', '1');
        inp.required = false;
        inp.type = 'hidden';
        inp.after(box);
        inp._sdBox = box; box._sdInput = inp;
        Object.defineProperty(inp, 'value', {
            configurable: true,
            get: function () { return inValue.get.call(this); },
            set: function (v) { inValue.set.call(this, v); box.value = dmyOfIso(inValue.get.call(this)); }
        });
        box.value = dmyOfIso(inValue.get.call(inp));

        function take() {                                       // typed text -> value (wrong text: message + old value back)
            var t = box.value.trim();
            if (!t) { setDate(inp, ''); return true; }
            var iso = isoOfDmy(t);
            if (!iso || !inRange(inp, iso)) {
                SD.toast(iso ? 'This date is not allowed here.' : 'Enter the date as DD/MM/YYYY (e.g. 25/12/2026).', true);
                box.value = dmyOfIso(inValue.get.call(inp));
                return false;
            }
            setDate(inp, iso);
            return true;
        }
        box.addEventListener('input', function (e) {           // 25122026 -> 25/12/2026 while typing
            if (!e.inputType || e.inputType.indexOf('insert') !== 0) return;
            var v = box.value;
            if (/^\d{2}$/.test(v) || /^\d{1,2}[\/\-.]\d{2}$/.test(v)) box.value = v + '/';
            else if (/^\d{8}$/.test(v)) box.value = v.slice(0, 2) + '/' + v.slice(2, 4) + '/' + v.slice(4);
        });
        box.addEventListener('change', take);
        box.addEventListener('keydown', function (e) {
            if (e.key === 'Enter') { if (!take()) e.preventDefault(); closeCal(); }
            else if (e.key === 'ArrowDown' || (e.key === 'Down')) { e.preventDefault(); openCal(box); }
            else if (e.key === 'Escape' || e.key === 'Tab') closeCal();
        });
        box.addEventListener('click', function () { openCal(box); });
    }

    // one small calendar for all date boxes
    var cal = null, calBox = null, calMonth = null;
    function buildCal() {
        cal = document.createElement('div');
        cal.className = 'sd-cal';
        cal.setAttribute('data-native', '');                     // its month / year lists stay normal drop downs
        cal.addEventListener('mousedown', function (e) { if (!e.target.closest('select')) e.preventDefault(); });   // keep the focus in the box
        cal.addEventListener('click', function (e) {
            var b = e.target.closest('button'); if (!b || !calBox) return;
            var inp = calBox._sdInput;
            if (b.dataset.nav) { calMonth.setMonth(calMonth.getMonth() + Number(b.dataset.nav)); drawCal(); return; }
            if (b.dataset.day) { setDate(inp, b.dataset.day); closeCal(); calBox && calBox.focus(); return; }
            if (b.dataset.act === 'today') { var t = isoOf(new Date()); if (inRange(inp, t)) setDate(inp, t); closeCal(); return; }
            if (b.dataset.act === 'clear') { setDate(inp, ''); closeCal(); return; }
            if (b.dataset.act === 'close') closeCal();
        });
        cal.addEventListener('change', function (e) {
            if (e.target.matches('.sd-cal-m')) calMonth.setMonth(Number(e.target.value));
            if (e.target.matches('.sd-cal-y')) calMonth.setFullYear(Number(e.target.value));
            drawCal();
        });
        document.body.appendChild(cal);
        document.addEventListener('mousedown', function (e) {
            if (cal.classList.contains('show') && !cal.contains(e.target) && e.target !== calBox) closeCal();
        }, true);
        window.addEventListener('resize', function () { if (cal.classList.contains('show')) placeCal(); });   // phone keyboard opens -> resize
        window.addEventListener('scroll', function (e) { if (cal.classList.contains('show') && !cal.contains(e.target)) placeCal(); }, true);
    }
    function drawCal() {
        var inp = calBox._sdInput, sel = inValue.get.call(inp), today = isoOf(new Date());
        var y = calMonth.getFullYear(), m = calMonth.getMonth();
        var first = new Date(y, m, 1), start = new Date(y, m, 1 - first.getDay());          // weeks start on Sunday
        var years = '';
        for (var yy = y - 60; yy <= y + 15; yy++) years += '<option value="' + yy + '"' + (yy === y ? ' selected' : '') + '>' + yy + '</option>';
        var html = '<div class="sd-cal-head">' +
            '<button type="button" data-nav="-1" title="Previous month" aria-label="Previous month">&lsaquo;</button>' +
            '<select class="sd-cal-m" aria-label="Month">' + MONTHS.map(function (n, i) { return '<option value="' + i + '"' + (i === m ? ' selected' : '') + '>' + n + '</option>'; }).join('') + '</select>' +
            '<select class="sd-cal-y" aria-label="Year">' + years + '</select>' +
            '<button type="button" data-nav="1" title="Next month" aria-label="Next month">&rsaquo;</button></div>' +
            '<div class="sd-cal-grid">' + ['Su', 'Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa'].map(function (d) { return '<span class="wd">' + d + '</span>'; }).join('');
        for (var i = 0; i < 42; i++) {
            var d = new Date(start.getFullYear(), start.getMonth(), start.getDate() + i), iso = isoOf(d);
            var cls = (d.getMonth() !== m ? 'out ' : '') + (iso === today ? 'today ' : '') + (iso === sel ? 'sel' : '');
            html += '<button type="button" data-day="' + iso + '" class="' + cls.trim() + '"' + (inRange(inp, iso) ? '' : ' disabled') +
                ' aria-label="' + pad2(d.getDate()) + '/' + pad2(d.getMonth() + 1) + '/' + d.getFullYear() + '">' + d.getDate() + '</button>';
        }
        html += '</div><div class="sd-cal-foot"><button type="button" data-act="today">Today</button>' +
            '<button type="button" data-act="clear">Clear</button><button type="button" data-act="close">Close</button></div>';
        cal.innerHTML = html;
    }
    function placeCal() {
        if (!calBox || !calBox.isConnected) { closeCal(); return; }
        var r = calBox.getBoundingClientRect(), w = cal.offsetWidth, h = cal.offsetHeight;
        var top = (r.bottom + 4 + h > window.innerHeight && r.top - 4 - h > 0) ? r.top - 4 - h : r.bottom + 4;
        cal.style.top = top + 'px';
        cal.style.left = Math.max(8, Math.min(r.left, window.innerWidth - w - 8)) + 'px';
    }
    function openCal(box) {
        if (box.disabled || box.readOnly) return;
        if (!cal) buildCal();
        calBox = box;
        var cur = dateOfIso(inValue.get.call(box._sdInput)) || new Date();
        calMonth = new Date(cur.getFullYear(), cur.getMonth(), 1);
        drawCal();
        cal.classList.add('show');
        placeCal();
    }
    function closeCal() { if (cal) cal.classList.remove('show'); calBox = null; }

    // ---------- run on the page + on everything added later ----------
    function enhance(root) {
        root = root || document;
        if (!root.querySelectorAll) return;
        root.querySelectorAll('input[type="date"]').forEach(enhanceDate);
        root.querySelectorAll('select').forEach(enhanceSelect);
    }
    SD.enhance = enhance;
    var enhanceTimer = null;
    function enhanceSoon() { clearTimeout(enhanceTimer); enhanceTimer = setTimeout(function () { enhance(document); }, 0); }   // after the page's own scripts
    document.addEventListener('DOMContentLoaded', function () {
        enhanceSoon();
        if (window.MutationObserver) {
            new MutationObserver(function (list) {
                for (var i = 0; i < list.length; i++) {
                    for (var j = 0; j < list[i].addedNodes.length; j++) {
                        var n = list[i].addedNodes[j];
                        if (n.nodeType === 1 && (n.matches('select, input[type="date"]') || n.querySelector('select, input[type="date"]'))) { enhanceSoon(); return; }
                    }
                }
            }).observe(document.body, { childList: true, subtree: true });
        }
    });
})();

//         =================    09/11/2026  ==========================

/* ===== [10] SIDEBAR STATE (append at the END of wwwroot/js/app-standard.js, after adminlte.js is loaded) =====
   WHY: AdminLTE runs "init()" on EVERY page load and on EVERY window resize:
        desktop -> it forces the sidebar open, mobile -> it forces it closed.
        So the user's choice was lost on each click/navigation, and on phones even a scroll (address-bar resize) closed it.
   NOW: the sidebar changes ONLY when the user clicks the toggle button (or the dark overlay on a phone).
        The choice is remembered (localStorage) and re-applied right after AdminLTE's own init.             */
(function () {
    'use strict';
    var KEY = 'sdSidebarCollapsed';        // desktop choice: '1' collapsed, '0' expanded
    var MKEY = 'sdSidebarMobileOpen';      // phone choice (sessionStorage)
    var BP = 992;                          // AdminLTE sidebar-expand-lg breakpoint
    var KEEP_OPEN_ON_MOBILE = false;       // true = phone drawer stays open after a click/navigation too
    var body = document.body;

    function lsGet(s, k) { try { return s.getItem(k); } catch (e) { return null; } }
    function lsSet(s, k, v) { try { s.setItem(k, v); } catch (e) { } }
    function isDesktop() { return window.innerWidth > BP; }
    function isCollapsed() { return body.classList.contains('sidebar-collapse'); }
    function apply(collapsed) {
        body.classList.toggle('sidebar-collapse', collapsed);
        body.classList.toggle('sidebar-open', !collapsed);
    }
    var lastW = window.innerWidth;
    var mobileOpen = KEEP_OPEN_ON_MOBILE && lsGet(sessionStorage, MKEY) === '1';

    function sync(widthChanged) {
        if (isDesktop()) apply(lsGet(localStorage, KEY) === '1');
        else if (widthChanged) { apply(!mobileOpen); }       // crossing the breakpoint
        else apply(!mobileOpen);                              // height-only resize (phone address bar) -> keep as is
    }
    function remember() {                                      // called AFTER a manual toggle
        if (isDesktop()) lsSet(localStorage, KEY, isCollapsed() ? '1' : '0');
        else { mobileOpen = !isCollapsed(); if (KEEP_OPEN_ON_MOBILE) lsSet(sessionStorage, MKEY, mobileOpen ? '1' : '0'); }
    }

    /* keep the active menu item highlighted even if the server could not match the url (virtual directory, trailing slash) */
    function norm(p) { p = (p || '/').toLowerCase(); return p.length > 1 ? p.replace(/\/+$/, '') : p; }
    function markActive() {
        var menu = document.querySelector('.app-sidebar .sidebar-menu');
        if (!menu || menu.querySelector('.nav-link.active')) return;
        var cur = norm(location.pathname), hit = null;
        menu.querySelectorAll('a.nav-link[href]').forEach(function (a) {
            var h = a.getAttribute('href');
            if (!h || h === '#' || h.indexOf('javascript') === 0) return;
            try { if (norm(new URL(a.href, location.href).pathname) === cur) hit = a; } catch (e) { }
        });
        if (!hit) return;
        hit.classList.add('active');
        var sub = hit.closest('.nav-treeview');
        if (sub) {
            var parent = sub.closest('.nav-item');
            parent.classList.add('menu-open');
            var pl = parent.querySelector(':scope > .nav-link'); if (pl) pl.classList.add('active');
        }
    }

    document.addEventListener('DOMContentLoaded', function () {   // registered AFTER AdminLTE's own handlers -> runs after them
        sync(false);
        markActive();
        window.addEventListener('resize', function () {           // registered AFTER AdminLTE's resize handler
            var changed = window.innerWidth !== lastW; lastW = window.innerWidth;
            sync(changed);
        });
        document.addEventListener('click', function (e) {         // manual toggle button / phone overlay only
            if (e.target.closest('[data-lte-toggle="sidebar"]') || e.target.closest('.sidebar-overlay')) remember();
        });
        document.addEventListener('touchend', function (e) {
            if (e.target.closest('.sidebar-overlay')) setTimeout(remember, 0);
        }, { passive: true });
    });
})();

