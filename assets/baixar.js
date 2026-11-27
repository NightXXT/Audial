// /baixar: before the launch (1 December 2026, Brasília) it goes to the countdown; after, it starts the download.
(function () {
  if (Date.now() < Date.parse("2026-12-01T00:00:00-03:00") && !/[?&]lancado\b/.test(location.search)) {
    location.replace("/#baixar");
    return;
  }
  setTimeout(function () { location.href = document.getElementById("dlLink").href; }, 400);
})();
