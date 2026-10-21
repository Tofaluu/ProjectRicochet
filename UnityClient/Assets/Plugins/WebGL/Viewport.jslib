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

  // The palette's colours for the loading page (index.html reads them on the next visit, before the game starts).
  Ricochet_SaveLoadingColors: function (json) {
    try { localStorage.setItem('ricochet.loadingColors', UTF8ToString(json)); } catch (e) { }
  }
});
