(function () {
  var rows = [].slice.call(document.querySelectorAll('#pf-tabs .pf-row')).slice(1);
  return JSON.stringify(rows.map(function (r) {
    return {
      name: (r.querySelector('.pf-n b') || {}).textContent,
      state: (r.querySelector('.pf-n span') || {}).textContent,
      memory: (r.querySelectorAll('.pf-num')[0] || {}).textContent
    };
  }), null, 1);
})()
