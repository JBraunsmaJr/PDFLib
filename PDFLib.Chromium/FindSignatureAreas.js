(data) => {
    const elements = document.querySelectorAll('[id^="signature-area-"]');
    const results = [];

    for (const el of elements) {
        const rect = el.getBoundingClientRect();
        results.push({
            id: el.id,
            x: rect.left + window.scrollX,
            y: rect.top + window.scrollY,
            width: rect.width,
            height: rect.height
        });

        if (data && data[el.id]) {
            const info = data[el.id];
            el.innerHTML = '';
            el.style.display = 'flex';
            el.style.flexDirection = 'column';
            el.style.justifyContent = 'center';
            el.style.alignItems = 'flex-start';
            el.style.padding = '2px 5px';
            el.style.boxSizing = 'border-box';
            el.style.fontSize = '8pt';
            el.style.fontFamily = 'Helvetica, Arial, sans-serif';
            el.style.color = 'black';
            el.style.lineHeight = '1.2';
            el.style.background = 'white'; // Cover "Signature Zone" if it was background or something

            const nameDiv = document.createElement('div');
            nameDiv.textContent = 'Digitally signed by ' + info.name;
            el.appendChild(nameDiv);

            const dateDiv = document.createElement('div');
            dateDiv.textContent = 'Date: ' + info.date;
            el.appendChild(dateDiv);
        }
    }
    return results;
}