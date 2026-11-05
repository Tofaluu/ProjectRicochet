// Browser builds: "Sign in with Google" for GoogleSignIn.cs. Google only hands out a sign-in token from its own button
// (Google Identity Services, https://accounts.google.com/gsi/client, loaded on first use), so the game's button opens a
// small dialog over the page holding Google's button. The result goes back through the callback: (1, token) signed in,
// (0, null) closed, (-1, message) Google's script couldn't load.
var RicochetGoogleSignIn = {
  $gsi__deps: ['$stringToUTF8', '$lengthBytesUTF8', 'malloc', 'free'],
  $gsi: {
    loading: null,
    clientId: null,
    pending: null, // the open dialog's finish(kind, text)

    load: function () {
      if (window.google && window.google.accounts && window.google.accounts.id)
        return Promise.resolve();
      if (!gsi.loading)
        gsi.loading = new Promise(function (resolve, reject) {
          var script = document.createElement('script');
          script.src = 'https://accounts.google.com/gsi/client';
          script.async = true;
          script.onload = function () { resolve(); };
          script.onerror = function () { gsi.loading = null; reject(new Error("Google sign-in couldn't load. Check your connection or ad blocker.")); };
          document.head.appendChild(script);
        });
      return gsi.loading;
    },

    // The dialog in the loading page's colours (index.html's --bg / --text / --muted / --track, the player's palette).
    dialog: function (title, body, finish) {
      var overlay = document.createElement('div');
      overlay.style.cssText = 'position:fixed;inset:0;z-index:10;display:flex;align-items:center;justify-content:center;' +
        'padding:16px;background:rgba(0,0,0,0.55);font-family:system-ui,-apple-system,"Segoe UI",sans-serif;';
      var card = document.createElement('div');
      card.style.cssText = 'box-sizing:border-box;width:100%;max-width:340px;padding:22px 20px 14px;border-radius:16px;' +
        'background:var(--bg,#1f1914);color:var(--text,#f6eedd);border:2px solid var(--track,#3f3932);text-align:center;';
      var heading = document.createElement('h2');
      heading.textContent = title;
      heading.style.cssText = 'margin:0 0 8px;font-size:20px;font-weight:700;';
      var text = document.createElement('p');
      text.textContent = body;
      text.style.cssText = 'margin:0 0 18px;font-size:14px;line-height:1.4;color:var(--muted,#a0998d);';
      var slot = document.createElement('div');
      slot.style.cssText = 'display:flex;justify-content:center;min-height:44px;';
      var cancel = document.createElement('button');
      cancel.textContent = 'Cancel';
      cancel.style.cssText = 'margin-top:12px;padding:8px 16px;border:0;background:none;color:var(--muted,#a0998d);' +
        'font:inherit;font-size:14px;cursor:pointer;';
      card.appendChild(heading);
      card.appendChild(text);
      card.appendChild(slot);
      card.appendChild(cancel);
      overlay.appendChild(card);
      document.body.appendChild(overlay);

      function onKey(e) { if (e.key === 'Escape') close(0, null); }
      function close(kind, value) {
        if (!overlay.parentNode) return;
        document.removeEventListener('keydown', onKey, true);
        overlay.parentNode.removeChild(overlay);
        var canvas = document.querySelector('canvas');
        if (canvas) canvas.focus();
        finish(kind, value);
      }
      cancel.onclick = function () { close(0, null); };
      overlay.onclick = function (e) { if (e.target === overlay) close(0, null); };
      document.addEventListener('keydown', onKey, true);
      return { slot: slot, close: close };
    }
  },

  Ricochet_GoogleSignIn: function (clientIdPtr, titlePtr, bodyPtr, callback) {
    var clientId = UTF8ToString(clientIdPtr), title = UTF8ToString(titlePtr), body = UTF8ToString(bodyPtr);
    function finish(kind, value) {
      gsi.pending = null;
      var ptr = 0;
      if (value) {
        var size = lengthBytesUTF8(value) + 1;
        ptr = _malloc(size);
        stringToUTF8(value, ptr, size);
      }
      {{{ makeDynCall('vii', 'callback') }}}(kind, ptr);
      if (ptr) _free(ptr);
    }
    if (gsi.pending) gsi.pending(0, null); // one dialog at a time
    var box = gsi.dialog(title, body, finish);
    gsi.pending = box.close;
    gsi.load().then(function () {
      if (gsi.pending !== box.close) return; // closed while loading
      if (gsi.clientId !== clientId) {
        gsi.clientId = clientId;
        google.accounts.id.initialize({
          client_id: clientId,
          callback: function (response) { if (gsi.pending) gsi.pending(1, response.credential); },
          ux_mode: 'popup',
          auto_select: false,
          cancel_on_tap_outside: false
        });
      }
      google.accounts.id.renderButton(box.slot, { theme: 'outline', size: 'large', text: 'signin_with', shape: 'rectangular', width: 260 });
    }, function (e) {
      box.close(-1, e.message);
    });
  }
};

autoAddDeps(RicochetGoogleSignIn, '$gsi');
mergeInto(LibraryManager.library, RicochetGoogleSignIn);
