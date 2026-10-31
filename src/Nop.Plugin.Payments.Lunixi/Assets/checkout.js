'use strict';
(() => {
    const config = document.getElementById('lunixi-checkout-config');
    const unavailable = document.getElementById('lunixi-checkout-unavailable');
    const fail = () => { if (unavailable) unavailable.hidden = false; };
    if (!config || !window.Lunixi || !window.Lunixi.checkout) { fail(); return; }
    try {
        const checkout = window.Lunixi.checkout.create(JSON.parse(config.textContent));
        checkout.mount('#lunixi-checkout-form');
    } catch { fail(); }
})();
