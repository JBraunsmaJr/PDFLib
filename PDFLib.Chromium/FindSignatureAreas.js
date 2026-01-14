(() => {
    const elements = document.querySelectorAll('[id^="signature-area-"]');

    return Array.from(elements).map(el => {
        const rect = el.getBoundingClientRect();
        return {
            id: el.id,
            // Add scroll offsets in case the page is long
            x: rect.left + window.scrollX,
            y: rect.top + window.scrollY,
            width: rect.width,
            height: rect.height
        };
    });
})();