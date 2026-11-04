// "Copiar chave": copies the Pix key and confirms on the button for a moment.
// (In its own file so the site's security policy can forbid inline scripts.)
(function () {
  var button = document.getElementById('copyPix');
  var key = document.getElementById('pixKey');
  if (!button || !key) return;
  function copied() {
    button.textContent = 'Chave copiada';
    setTimeout(function () { button.textContent = 'Copiar chave'; }, 2200);
  }
  function selectAndCopy() {
    var range = document.createRange();
    range.selectNodeContents(key);
    var selection = window.getSelection();
    selection.removeAllRanges();
    selection.addRange(range);
    try { if (document.execCommand('copy')) copied(); } catch (e) { /* the key stays selected to copy by hand */ }
  }
  button.addEventListener('click', function () {
    var text = key.textContent.trim();
    if (navigator.clipboard && navigator.clipboard.writeText) navigator.clipboard.writeText(text).then(copied, selectAndCopy);
    else selectAndCopy();
  });
})();
