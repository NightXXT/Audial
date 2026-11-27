// /juntar?sala=AUD-XXXXX-XXXXX (the "Juntar-se" button on Discord): opens the room in Audial through audial://.
// The code is checked against the app's own format before it goes anywhere; nothing is sent to the server.
(function () {
  var match = /^AUD-?([A-Z0-9]{5})-?([A-Z0-9]{5})$/.exec((new URLSearchParams(location.search).get('sala') || '').trim().toUpperCase());
  var text = document.getElementById('joinText');
  if (!match) {
    text.textContent = 'Esse link de sala não está completo. Peça para quem te chamou mandar de novo, ou entre no Audial e cole o código em Ouvir junto.';
    return;
  }
  var code = 'AUD-' + match[1] + '-' + match[2];
  var link = 'audial://sala/' + code;
  var codeBox = document.getElementById('joinCode');
  var open = document.getElementById('joinOpen');
  var hint = document.getElementById('joinHint');
  codeBox.textContent = code;
  codeBox.hidden = false;
  open.href = link;
  open.hidden = false;
  open.addEventListener('click', function () {
    setTimeout(function () { hint.hidden = false; }, 1500);
  });
  // tries once by itself; the browser asks before opening the app
  setTimeout(function () {
    location.href = link;
    setTimeout(function () { hint.hidden = false; }, 1500);
  }, 600);
})();
