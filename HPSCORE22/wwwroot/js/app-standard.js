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
         every sortable <th class="sortable" data-sort="xxx">                                              */
    SD.initList = function () {
        var tabs = document.querySelectorAll('.sd-tab'), panes = document.querySelectorAll('.sd-pane'),
            tiles = document.querySelectorAll('.sd-tile');
        if (!panes.length) return;
        var dayKey = 'sdDay:' + location.pathname, dayFilter = remember(dayKey) || 'all', sortKey = null, sortDir = 1;
        var search = $('sdSearch'), dateBox = $('sdDate'), clear = $('sdClear'), resetSort = $('sdResetSort');

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

        function refresh() {
            var pane = activePane(), q = search ? search.value.trim().toLowerCase() : '', d = dateBox ? dateBox.value : '';
            var rows = Array.prototype.slice.call(pane.querySelectorAll('tbody tr:not(.sd-total)'));      // a Total row is never filtered / sorted

            var c = { all: rows.length, today: 0, future: 0, past: 0 };            // numbers on the tiles
            rows.forEach(function (tr) { if (c[tr.dataset.day] !== undefined) c[tr.dataset.day]++; });
            Object.keys(c).forEach(function (k) { var n = document.querySelector('[data-num="' + k + '"]'); if (n) n.textContent = c[k]; });
            tiles.forEach(function (t) { t.classList.toggle('active', t.dataset.day === dayFilter); });

            var shown = 0;
            rows.forEach(function (tr) {
                var ok = (dayFilter === 'all' || tr.dataset.day === dayFilter) &&
                    (!d || tr.dataset.date === d) &&
                    (!q || (tr.dataset.search || '').indexOf(q) !== -1);
                tr.style.display = ok ? '' : 'none';
                if (ok) shown++;
            });
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
})();
