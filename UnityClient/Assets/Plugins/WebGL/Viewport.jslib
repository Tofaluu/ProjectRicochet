// Browser builds: the canvas's width in CSS pixels for Viewport.cs. Phones and tablets draw several device pixels per CSS
// pixel, so the UI is sized from this (screen pixels alone would make it tiny there).
mergeInto(LibraryManager.library, {
  Ricochet_CanvasCssWidth: function () {
    var canvas = (typeof Module !== 'undefined' && Module.canvas) || document.querySelector('canvas');
    return canvas ? canvas.clientWidth : 0;
  },

  // 1 when the main pointer is a finger (phones, tablets): Unity's WebGL input reports a touchscreen on desktops too.
  Ricochet_TouchPrimary: function () {
    var mq = window.matchMedia;
    if (!mq) return 0;
    if (mq('(pointer: coarse)').matches) return 1;
    return (navigator.maxTouchPoints > 0 && !mq('(pointer: fine)').matches) ? 1 : 0;
  },

  // The part of the page the on-screen keyboard leaves visible (CSS pixels from the top): the canvas keeps its size when the
  // keyboard opens, only the visual viewport shrinks (and may pan). -1 without the visualViewport API.
  Ricochet_VisibleTop: function () {
    var v = window.visualViewport;
    return v ? v.offsetTop : -1;
  },

  Ricochet_VisibleHeight: function () {
    var v = window.visualViewport;
    return v ? v.height : -1;
  },

  // How far the page would scroll for the wheel events since the last call, in CSS pixels (what the browser itself would
  // scroll). Unity's wheel events give one step per event, and a trackpad sends dozens of tiny ones per swipe.
  Ricochet_TakeWheel: function () {
    if (!window.ricochetWheel) {
      window.ricochetWheel = { dy: 0, at: 0 };
      window.addEventListener('wheel', function (e) {
        var w = window.ricochetWheel, now = performance.now();
        if (now - w.at > 250) w.dy = 0; // an old gesture nobody took (scrolled over the board)
        w.at = now;
        w.dy += e.deltaMode === 1 ? e.deltaY * 16 : e.deltaMode === 2 ? e.deltaY * window.innerHeight : e.deltaY;
      }, { capture: true, passive: true });
      return 0;
    }
    var dy = window.ricochetWheel.dy;
    window.ricochetWheel.dy = 0;
    return dy;
  },

  // The palette's colours for the loading page (index.html reads them on the next visit, before the game starts).
  Ricochet_SaveLoadingColors: function (json) {
    try { localStorage.setItem('ricochet.loadingColors', UTF8ToString(json)); } catch (e) { }
  }
});
