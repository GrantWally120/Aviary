(function () {
  var rows = [].slice.call(document.querySelectorAll('#pf-procs .pf-row'));
  var head = rows.shift();
  var sorting = [].slice.call(head.querySelectorAll('.pf-num[data-on="true"]')).map(function (c) { return c.getAttribute('data-sort'); });
  var cols = [].slice.call(head.querySelectorAll('.pf-num')).map(function (c) { return c.textContent.trim(); });
  var out = rows.map(function (r) {
    return {
      kind: r.classList.contains('pf-kid') ? 'tab' : r.classList.contains('pf-tot') ? 'total' : 'process',
      name: (r.querySelector('.pf-n b') || {}).textContent,
      cells: [].slice.call(r.querySelectorAll('.pf-num')).map(function (c) { return c.textContent.trim(); }),
      heat: [].slice.call(r.querySelectorAll('.pf-num')).map(function (c) { return c.getAttribute('data-heat') || '' }).join(',')
    };
  });
  return JSON.stringify({ sortingBy: sorting, columns: cols, rows: out }, null, 1);
})()
