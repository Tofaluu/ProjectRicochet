// Browser builds: copies text to the system clipboard for Clipboard.cs (Unity's own clipboard stays inside the page).
// navigator.clipboard needs a secure page and a recent click (the player just pressed "Copy result"); older browsers
// fall back to a hidden textarea and execCommand.
mergeInto(LibraryManager.library, {
  Ricochet_CopyText: function (ptr) {
    var text = UTF8ToString(ptr);
    function fallback() {
      var area = document.createElement('textarea');
      area.value = text;
      area.style.position = 'fixed';
      area.style.opacity = '0';
      document.body.appendChild(area);
      area.select();
      try { document.execCommand('copy'); } catch (e) { }
      document.body.removeChild(area);
      var canvas = document.querySelector('canvas');
      if (canvas) canvas.focus();
    }
    if (navigator.clipboard && navigator.clipboard.writeText)
      navigator.clipboard.writeText(text).catch(fallback);
    else
      fallback();
  }
});
