(function () {
  var sec = document.querySelector('#panes > section[data-sec="news"]');
  if (!sec) { return JSON.stringify({ section: false }); }
  var cols = [].slice.call(sec.querySelectorAll('.nw__col')).map(function (c) {
    return {
      heading: c.querySelector('h3').textContent,
      items: [].slice.call(c.querySelectorAll('.nw__item')).map(function (i) {
        return { src: i.querySelector('.nw__src b').textContent, when: i.querySelector('.nw__src span').textContent,
                 title: i.querySelector('.nw__title').textContent, url: i.getAttribute('data-url') };
      })
    };
  });
  return JSON.stringify({
    section: true,
    meta: (sec.querySelector('.nw__meta') || {}).textContent,
    down: (sec.querySelector('.nw__down') || {}).textContent || null,
    empty: (sec.querySelector('.empty') || {}).textContent || null,
    imgInsideNews: sec.querySelectorAll('img').length,
    anyJavascriptLink: !!sec.querySelector('[data-url^="javascript"]'),
    allButton: !!sec.querySelector('[data-news-all]'),
    cols: cols
  }, null, 1);
})()
