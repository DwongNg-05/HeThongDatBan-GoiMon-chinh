(() => {
    const map = document.querySelector('.snapshot-map');
    if (!map || map.dataset.loadFailed === 'true') return;
    // Two animation frames after handlers attach: all tiles have layout and a paint opportunity.
    requestAnimationFrame(() => requestAnimationFrame(() => {
        const tiles = map.querySelectorAll('[data-table-code]');
        if (tiles.length !== Number(map.dataset.tableCount) || map.dataset.syncReady !== 'true' || map.dataset.detailsReady !== 'true') return;
        if (Array.from(tiles).some(tile => tile.getBoundingClientRect().width === 0)) return;
        const elapsed = Math.round(performance.now());
        performance.measure('table-map-ready', { start: 0, end: elapsed });
        map.dataset.readyMs = String(elapsed);
        window.dispatchEvent(new CustomEvent('tablemap:ready', { detail: { milliseconds: elapsed, tableCount: tiles.length } }));
    }));
})();
