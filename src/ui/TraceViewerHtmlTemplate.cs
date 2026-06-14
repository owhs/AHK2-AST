using System;

namespace AHK2AST
{
    public static class TraceViewerHtmlTemplate
    {
        public static string Html = @"<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <title>AHK# AST Trace Viewer</title>
    <link href='https://fonts.googleapis.com/css2?family=Inter:wght@300;400;500;600;700&family=JetBrains+Mono:wght@400;500&display=swap' rel='stylesheet'>
    <style>
        :root {
            --bg-mantle: #11111b;
            --bg-base: #1e1e2e;
            --bg-surface: #313244;
            --bg-surface-hover: #45475a;
            --border: #45475a;
            --text: #cdd6f4;
            --text-sub: #a6adc8;
            --text-muted: #585b70;
            --accent: #89b4fa;
            --accent-green: #a6e3a1;
            --accent-yellow: #f9e2af;
            --accent-peach: #fab387;
            --accent-red: #f38ba8;
            --font-sans: 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
            --font-mono: 'JetBrains Mono', monospace;
        }

        * {
            box-sizing: border-box;
            margin: 0;
            padding: 0;
        }

        body {
            font-family: var(--font-sans);
            background-color: var(--bg-base);
            color: var(--text);
            height: 100vh;
            display: flex;
            flex-direction: column;
            overflow: hidden;
        }

        header {
            background-color: var(--bg-mantle);
            border-bottom: 1px solid var(--border);
            padding: 10px 20px;
            display: flex;
            justify-content: space-between;
            align-items: center;
            height: 56px;
            flex-shrink: 0;
        }

        .header-left {
            display: flex;
            align-items: center;
            gap: 12px;
        }

        .header-title {
            font-size: 16px;
            font-weight: 700;
            letter-spacing: 0.5px;
            background: linear-gradient(90deg, #89b4fa, #b4befe);
            -webkit-background-clip: text;
            -webkit-text-fill-color: transparent;
        }

        .header-badge {
            background-color: var(--bg-surface);
            color: var(--text-sub);
            padding: 4px 8px;
            border-radius: 6px;
            font-size: 11px;
            font-weight: 600;
            border: 1px solid var(--border);
        }

        .header-stats {
            display: flex;
            gap: 24px;
            font-size: 13px;
        }

        .stat-item {
            display: flex;
            flex-direction: column;
            align-items: flex-end;
        }

        .stat-label {
            color: var(--text-muted);
            font-size: 10px;
            font-weight: 700;
            text-transform: uppercase;
        }

        .stat-value {
            font-weight: 600;
            color: var(--accent-green);
        }

        .workspace-container {
            display: flex;
            flex: 1;
            overflow: hidden;
        }

        .sidebar {
            width: 340px;
            background-color: var(--bg-mantle);
            border-right: 1px solid var(--border);
            display: flex;
            flex-direction: column;
            flex-shrink: 0;
        }

        .sidebar-section {
            padding: 16px;
            border-bottom: 1px solid var(--border);
        }

        .sidebar-section-title {
            font-size: 11px;
            font-weight: 700;
            text-transform: uppercase;
            color: var(--text-sub);
            margin-bottom: 12px;
            letter-spacing: 0.5px;
        }

        .search-box {
            position: relative;
            width: 100%;
        }

        .search-input {
            width: 100%;
            background-color: var(--bg-surface);
            border: 1px solid var(--border);
            border-radius: 8px;
            padding: 8px 12px;
            color: var(--text);
            font-family: var(--font-sans);
            font-size: 13px;
            outline: none;
            transition: border-color 0.2s;
        }

        .search-input:focus {
            border-color: var(--accent);
        }

        .checklist {
            display: flex;
            flex-direction: column;
            gap: 8px;
        }

        .checklist-item {
            display: flex;
            align-items: center;
            gap: 10px;
            cursor: pointer;
            user-select: none;
            font-size: 13px;
        }

        .checklist-item input {
            display: none;
        }

        .custom-checkbox {
            width: 16px;
            height: 16px;
            border: 1px solid var(--border);
            border-radius: 4px;
            display: flex;
            align-items: center;
            justify-content: center;
            transition: all 0.2s;
            background-color: var(--bg-base);
        }

        .checklist-item input:checked + .custom-checkbox {
            background-color: var(--accent);
            border-color: var(--accent);
        }

        .checklist-item input:checked + .custom-checkbox::after {
            content: '✓';
            color: var(--bg-mantle);
            font-size: 11px;
            font-weight: 700;
        }

        .category-dot {
            width: 8px;
            height: 8px;
            border-radius: 50%;
            display: inline-block;
        }

        .dot-line { background-color: #585b70; }
        .dot-func { background-color: #89b4fa; }
        .dot-class { background-color: #cba6f7; }
        .dot-event { background-color: #fab387; }

        .details-container {
            flex: 1;
            overflow-y: auto;
            padding: 16px;
            display: flex;
            flex-direction: column;
            gap: 12px;
        }

        .detail-row {
            display: flex;
            flex-direction: column;
            gap: 4px;
        }

        .detail-lbl {
            font-size: 10px;
            font-weight: 700;
            color: var(--text-muted);
            text-transform: uppercase;
        }

        .detail-val {
            font-size: 13px;
            font-family: var(--font-mono);
            word-break: break-all;
            background-color: var(--bg-surface);
            padding: 6px 10px;
            border-radius: 6px;
            border: 1px solid var(--border);
        }

        .main-content {
            flex: 1;
            display: flex;
            flex-direction: column;
            overflow: hidden;
            position: relative;
        }

        .tab-bar {
            background-color: var(--bg-mantle);
            border-bottom: 1px solid var(--border);
            display: flex;
            padding: 0 16px;
            height: 40px;
            flex-shrink: 0;
        }

        .tab-btn {
            background: none;
            border: none;
            border-bottom: 2px solid transparent;
            color: var(--text-sub);
            padding: 0 16px;
            font-family: var(--font-sans);
            font-size: 13px;
            font-weight: 500;
            cursor: pointer;
            height: 100%;
            display: flex;
            align-items: center;
            transition: all 0.2s;
        }

        .tab-btn:hover {
            color: var(--text);
            background-color: rgba(255, 255, 255, 0.03);
        }

        .tab-btn.active {
            color: var(--accent);
            border-bottom-color: var(--accent);
        }

        .tab-content {
            flex: 1;
            overflow: hidden;
            position: relative;
            display: none;
        }

        .tab-content.active {
            display: flex;
            flex-direction: column;
        }

        #flame-timeline-tab {
            position: relative;
        }

        .flamegraph-controls {
            position: absolute;
            top: 12px;
            right: 20px;
            display: flex;
            gap: 6px;
            z-index: 10;
            background-color: rgba(17, 17, 27, 0.85);
            padding: 4px;
            border-radius: 8px;
            border: 1px solid var(--border);
            backdrop-filter: blur(4px);
        }

        .control-btn {
            background-color: var(--bg-surface);
            border: 1px solid var(--border);
            color: var(--text);
            padding: 4px 10px;
            border-radius: 4px;
            font-family: var(--font-sans);
            font-size: 11px;
            font-weight: 600;
            cursor: pointer;
            transition: background 0.2s;
        }

        .control-btn:hover {
            background-color: var(--bg-surface-hover);
        }

        .canvas-container {
            flex: 1;
            overflow: auto;
            position: relative;
            background-color: var(--bg-base);
        }

        #flame-canvas {
            display: block;
            cursor: grab;
        }

        #flame-canvas:active {
            cursor: grabbing;
        }

        .tooltip {
            position: absolute;
            background-color: rgba(17, 17, 27, 0.95);
            border: 1px solid var(--border);
            border-radius: 8px;
            padding: 10px 12px;
            font-size: 12px;
            font-family: var(--font-sans);
            color: var(--text);
            pointer-events: none;
            display: none;
            z-index: 100;
            box-shadow: 0 4px 16px rgba(0,0,0,0.5);
            max-width: 320px;
        }

        .tooltip-title {
            font-weight: 700;
            color: var(--accent);
            margin-bottom: 4px;
            word-break: break-all;
        }

        .tooltip-row {
            display: flex;
            justify-content: space-between;
            margin-top: 4px;
            gap: 16px;
        }

        .tooltip-lbl {
            color: var(--text-sub);
        }

        .tooltip-val {
            font-weight: 600;
            font-family: var(--font-mono);
        }

        .tree-container {
            flex: 1;
            overflow-y: auto;
            padding: 16px;
            font-family: var(--font-mono);
            font-size: 12px;
        }

        .tree-node {
            display: flex;
            flex-direction: column;
            margin-left: 20px;
            position: relative;
        }

        .tree-node-header {
            display: flex;
            align-items: center;
            padding: 3px 6px;
            border-radius: 4px;
            cursor: pointer;
            user-select: none;
            gap: 6px;
        }

        .tree-node-header:hover {
            background-color: var(--bg-surface);
        }

        .tree-toggle {
            width: 14px;
            height: 14px;
            display: flex;
            align-items: center;
            justify-content: center;
            font-size: 10px;
            color: var(--text-sub);
        }

        .tree-node-content {
            display: flex;
            align-items: center;
            gap: 8px;
            flex: 1;
        }

        .tree-timing {
            color: var(--accent-green);
            font-weight: 600;
        }

        .tree-file {
            color: var(--text-muted);
            font-size: 11px;
        }

        .tree-bar-outer {
            width: 60px;
            height: 6px;
            background-color: var(--bg-surface);
            border-radius: 3px;
            overflow: hidden;
            display: inline-block;
        }

        .tree-bar-inner {
            height: 100%;
            background-color: var(--accent);
        }

        .table-container {
            flex: 1;
            overflow-y: auto;
            padding: 16px;
        }

        .stats-table {
            width: 100%;
            border-collapse: collapse;
            font-size: 13px;
            text-align: left;
        }

        .stats-table th {
            background-color: var(--bg-mantle);
            color: var(--text-sub);
            font-weight: 600;
            padding: 12px 16px;
            border-bottom: 2px solid var(--border);
            cursor: pointer;
            user-select: none;
        }

        .stats-table th:hover {
            color: var(--text);
            background-color: var(--bg-surface);
        }

        .stats-table td {
            padding: 10px 16px;
            border-bottom: 1px solid var(--border);
            font-family: var(--font-mono);
        }

        .stats-table tbody tr:hover {
            background-color: rgba(255, 255, 255, 0.02);
        }

        .tbl-name {
            color: var(--accent);
            font-family: var(--font-sans) !important;
            font-weight: 500;
        }

        .empty-state {
            padding: 40px;
            text-align: center;
            color: var(--text-sub);
            font-size: 14px;
        }
    </style>
</head>
<body>

    <header>
        <div class='header-left'>
            <span class='header-title'>AHK# AST TRACE VISUALIZER</span>
            <span class='header-badge' id='file-badge'>trace_log.json</span>
        </div>
        <div class='header-stats'>
            <div class='stat-item'>
                <span class='stat-label'>Total Duration</span>
                <span class='stat-value' id='stat-duration'>0.00 ms</span>
            </div>
            <div class='stat-item'>
                <span class='stat-label'>Total Call Nodes</span>
                <span class='stat-value' id='stat-calls'>0</span>
            </div>
        </div>
    </header>

    <div class='workspace-container'>
        <div class='sidebar'>
            <div class='sidebar-section'>
                <div class='sidebar-section-title'>Filter & Search</div>
                <div class='search-box'>
                    <input type='text' id='search-input' class='search-input' placeholder='Search nodes... (e.g. MyFunc)'>
                </div>
            </div>

            <div class='sidebar-section'>
                <div class='sidebar-section-title'>Trace Categories</div>
                <div class='checklist'>
                    <label class='checklist-item'>
                        <input type='checkbox' id='chk-functions' checked>
                        <span class='custom-checkbox'></span>
                        <span class='category-dot dot-func'></span>
                        <span>Functions</span>
                    </label>
                    <label class='checklist-item'>
                        <input type='checkbox' id='chk-classes' checked>
                        <span class='custom-checkbox'></span>
                        <span class='category-dot dot-class'></span>
                        <span>Class Methods</span>
                    </label>
                    <label class='checklist-item'>
                        <input type='checkbox' id='chk-events' checked>
                        <span class='custom-checkbox'></span>
                        <span class='category-dot dot-event'></span>
                        <span>Events (Hotkeys)</span>
                    </label>
                    <label class='checklist-item'>
                        <input type='checkbox' id='chk-lines' checked>
                        <span class='custom-checkbox'></span>
                        <span class='category-dot dot-line'></span>
                        <span>Individual Lines</span>
                    </label>
                </div>
            </div>

            <div class='sidebar-section' style='flex: 1; display: flex; flex-direction: column; overflow: hidden;'>
                <div class='sidebar-section-title'>Selection Details</div>
                <div class='details-container' id='details-container'>
                    <div class='empty-state' style='padding: 20px 0;'>Click any node to view parameters, files, and exact source code statement.</div>
                </div>
            </div>
        </div>

        <div class='main-content'>
            <div class='tab-bar'>
                <button class='tab-btn active' id='btn-tab-flame'>Flame Timeline</button>
                <button class='tab-btn' id='btn-tab-tree'>Hierarchical Tree</button>
                <button class='tab-btn' id='btn-tab-stats'>Performance Hotspots</button>
            </div>

            <div id='flame-timeline-tab' class='tab-content active'>
                <div class='flamegraph-controls'>
                    <button class='control-btn' id='btn-zoom-in'>Zoom In</button>
                    <button class='control-btn' id='btn-zoom-out'>Zoom Out</button>
                    <button class='control-btn' id='btn-zoom-reset'>Reset</button>
                    <button class='control-btn' id='btn-zoom-fit'>Fit Width</button>
                </div>
                <div class='canvas-container' id='canvas-container'>
                    <canvas id='flame-canvas'></canvas>
                </div>
                <div id='flame-tooltip' class='tooltip'></div>
            </div>

            <div id='call-tree-tab' class='tab-content'>
                <div class='tree-container' id='tree-root-container'></div>
            </div>

            <div id='performance-hotspots-tab' class='tab-content'>
                <div class='table-container'>
                    <table class='stats-table'>
                        <thead>
                            <tr>
                                <th id='th-name'>Node Name</th>
                                <th id='th-count'>Calls</th>
                                <th id='th-cumulative'>Total Time</th>
                                <th id='th-self'>Self Time</th>
                                <th id='th-avg'>Avg Time</th>
                            </tr>
                        </thead>
                        <tbody id='hotspots-tbody'></tbody>
                    </table>
                </div>
            </div>
        </div>
    </div>

    <script>
        const rawBase64 = '/* TRACE_DATA_PLACEHOLDER */';
        let rawTraceData = [];
        try {
            rawTraceData = JSON.parse(atob(rawBase64));
        } catch(e) {
            console.error('Failed to parse base64 trace:', e);
        }

        let filteredRoots = [];
        let flatFilteredItems = [];
        let selectedItem = null;
        let totalTime = 0.0;
        let totalCallsCount = 0;
        let currentSort = { column: 'cumulative', desc: true };

        const canvas = document.getElementById('flame-canvas');
        const ctx = canvas.getContext('2d');
        let xScale = 1.0;
        let panX = 0;
        let panY = 0;
        let rowHeight = 24;
        let padding = 1;
        let hoveredItem = null;
        let isDragging = false;
        let dragStart = { x: 0, y: 0 };
        let dragPan = { x: 0, y: 0 };
        let maxTimelineDepth = 0;

        document.addEventListener('DOMContentLoaded', () => {
            setupEventListeners();
            processData();
            fitWidth();
            renderAll();
        });

        function setupEventListeners() {
            ['chk-functions', 'chk-classes', 'chk-events', 'chk-lines'].forEach(id => {
                document.getElementById(id).addEventListener('change', () => {
                    processData();
                    renderAll();
                });
            });

            document.getElementById('search-input').addEventListener('input', () => {
                processData();
                renderAll();
            });

            canvas.addEventListener('mousedown', (e) => {
                isDragging = true;
                dragStart.x = e.clientX;
                dragStart.y = e.clientY;
                dragPan.x = panX;
                dragPan.y = panY;
            });

            window.addEventListener('mouseup', () => {
                isDragging = false;
            });

            canvas.addEventListener('mousemove', (e) => {
                const rect = canvas.getBoundingClientRect();
                const mouseX = e.clientX - rect.left;
                const mouseY = e.clientY - rect.top;

                if (isDragging) {
                    const dx = e.clientX - dragStart.x;
                    const dy = e.clientY - dragStart.y;
                    panX = dragPan.x + dx;
                    panY = dragPan.y + dy;
                    if (panY > 0) panY = 0;
                    drawFlamegraph();
                    updateTooltip(mouseX, mouseY);
                } else {
                    const prevHovered = hoveredItem;
                    hoveredItem = getItemAtPosition(mouseX, mouseY);
                    if (prevHovered !== hoveredItem) {
                        drawFlamegraph();
                    }
                    updateTooltip(mouseX, mouseY);
                }
            });

            canvas.addEventListener('click', (e) => {
                const rect = canvas.getBoundingClientRect();
                const mouseX = e.clientX - rect.left;
                const mouseY = e.clientY - rect.top;
                const clicked = getItemAtPosition(mouseX, mouseY);
                if (clicked) {
                    selectItem(clicked);
                }
            });

            canvas.addEventListener('dblclick', (e) => {
                const rect = canvas.getBoundingClientRect();
                const mouseX = e.clientX - rect.left;
                const mouseY = e.clientY - rect.top;
                const clicked = getItemAtPosition(mouseX, mouseY);
                if (clicked && clicked.Elapsed > 0.001) {
                    const containerWidth = canvas.parentNode.clientWidth;
                    xScale = (containerWidth - 40) / clicked.Elapsed;
                    panX = 20 - clicked.Start * xScale;
                    drawFlamegraph();
                }
            });

            canvas.addEventListener('wheel', (e) => {
                e.preventDefault();
                const rect = canvas.getBoundingClientRect();
                const mouseX = e.clientX - rect.left;
                const timelineTime = (mouseX - panX) / xScale;

                const zoomFactor = e.deltaY < 0 ? 1.15 : 0.85;
                xScale *= zoomFactor;
                panX = mouseX - timelineTime * xScale;
                drawFlamegraph();
            });

            window.addEventListener('resize', () => {
                resizeCanvas();
                drawFlamegraph();
            });

            // Bind tab buttons
            document.getElementById('btn-tab-flame').addEventListener('click', () => switchTab('flame-timeline-tab'));
            document.getElementById('btn-tab-tree').addEventListener('click', () => switchTab('call-tree-tab'));
            document.getElementById('btn-tab-stats').addEventListener('click', () => switchTab('performance-hotspots-tab'));

            // Bind zoom controls
            document.getElementById('btn-zoom-in').addEventListener('click', () => zoomFlame(1.3));
            document.getElementById('btn-zoom-out').addEventListener('click', () => zoomFlame(0.7));
            document.getElementById('btn-zoom-reset').addEventListener('click', () => resetZoom());
            document.getElementById('btn-zoom-fit').addEventListener('click', () => fitWidth());

            // Bind table headers for sorting
            document.getElementById('th-name').addEventListener('click', () => sortHotspots('name'));
            document.getElementById('th-count').addEventListener('click', () => sortHotspots('count'));
            document.getElementById('th-cumulative').addEventListener('click', () => sortHotspots('cumulative'));
            document.getElementById('th-self').addEventListener('click', () => sortHotspots('self'));
            document.getElementById('th-avg').addEventListener('click', () => sortHotspots('avg'));
        }

        function switchTab(tabId) {
            document.querySelectorAll('.tab-btn').forEach(btn => btn.classList.remove('active'));
            document.querySelectorAll('.tab-content').forEach(content => content.classList.remove('active'));

            if (tabId === 'flame-timeline-tab') document.getElementById('btn-tab-flame').classList.add('active');
            else if (tabId === 'call-tree-tab') document.getElementById('btn-tab-tree').classList.add('active');
            else if (tabId === 'performance-hotspots-tab') document.getElementById('btn-tab-stats').classList.add('active');

            document.getElementById(tabId).classList.add('active');

            if (tabId === 'flame-timeline-tab') {
                resizeCanvas();
                drawFlamegraph();
            }
        }

        function getItemType(node) {
            if (node.Type === 'Line') return 'Line';
            if (node.Name && (node.Name.startsWith('Hotkey:') || node.Name.startsWith('Hotstring:') || node.Name.startsWith('Event:'))) return 'Event';
            if (node.Name && node.Name.includes('.')) return 'Class';
            return 'Function';
        }

        function processData() {
            const chkFunc = document.getElementById('chk-functions').checked;
            const chkClass = document.getElementById('chk-classes').checked;
            const chkEvent = document.getElementById('chk-events').checked;
            const chkLine = document.getElementById('chk-lines').checked;
            const search = document.getElementById('search-input').value.toLowerCase().trim();

            flatFilteredItems = [];
            filteredRoots = [];

            function filterNode(rawNode, parentNode, depth) {
                const type = getItemType(rawNode);
                let visible = true;

                if (type === 'Line' && !chkLine) visible = false;
                else if (type === 'Function' && !chkFunc) visible = false;
                else if (type === 'Class' && !chkClass) visible = false;
                else if (type === 'Event' && !chkEvent) visible = false;

                if (visible && search !== '') {
                    const nameMatch = rawNode.Name && rawNode.Name.toLowerCase().includes(search);
                    const codeMatch = rawNode.Code && rawNode.Code.toLowerCase().includes(search);
                    if (!nameMatch && !codeMatch) visible = false;
                }

                if (visible) {
                    const item = {
                        Name: rawNode.Name || (type === 'Line' ? 'Line ' + rawNode.Line : 'Node'),
                        Type: type,
                        Start: Number(rawNode.Start) || 0.0,
                        Elapsed: Number(rawNode.Elapsed) || 0.0,
                        File: rawNode.File || 'Main',
                        Line: rawNode.Line || 0,
                        Code: rawNode.Code || '',
                        Params: rawNode.Params || null,
                        Error: rawNode.Error || null,
                        Timestamp: rawNode.Timestamp || '',
                        Children: [],
                        Parent: parentNode,
                        Depth: depth
                    };

                    flatFilteredItems.push(item);

                    if (parentNode) {
                        parentNode.Children.push(item);
                    } else {
                        filteredRoots.push(item);
                    }

                    if (rawNode.Children) {
                        rawNode.Children.forEach(child => filterNode(child, item, depth + 1));
                    }
                } else {
                    if (rawNode.Children) {
                        rawNode.Children.forEach(child => filterNode(child, parentNode, depth));
                    }
                }
            }

            rawTraceData.forEach(root => filterNode(root, null, 0));

            totalCallsCount = flatFilteredItems.length;
            totalTime = 0.0;
            maxTimelineDepth = 0;

            flatFilteredItems.forEach(item => {
                const end = item.Start + item.Elapsed;
                if (end > totalTime) totalTime = end;
                if (item.Depth > maxTimelineDepth) maxTimelineDepth = item.Depth;
            });

            document.getElementById('stat-duration').innerText = totalTime.toFixed(2) + ' ms';
            document.getElementById('stat-calls').innerText = totalCallsCount.toLocaleString();
        }

        function resizeCanvas() {
            const container = document.getElementById('canvas-container');
            const dpr = window.devicePixelRatio || 1;
            const width = container.clientWidth;
            const height = Math.max(container.clientHeight, (maxTimelineDepth + 2) * rowHeight + 40);

            canvas.width = width * dpr;
            canvas.height = height * dpr;
            canvas.style.width = width + 'px';
            canvas.style.height = height + 'px';
            ctx.scale(dpr, dpr);
        }

        function drawFlamegraph() {
            if (canvas.width === 0) resizeCanvas();
            ctx.clearRect(0, 0, canvas.width, canvas.height);

            ctx.fillStyle = '#242535';
            ctx.strokeStyle = '#313244';
            ctx.lineWidth = 1;
            ctx.font = '10px var(--font-sans)';

            const step = Math.pow(10, Math.floor(Math.log10(100 / xScale)));
            const startMs = -panX / xScale;
            const endMs = (canvas.width - panX) / xScale;
            const markerStep = step > 0.01 ? step : 0.01;

            const firstMarker = Math.floor(startMs / markerStep) * markerStep;
            for (let t = firstMarker; t <= endMs; t += markerStep) {
                const x = t * xScale + panX;
                ctx.beginPath();
                ctx.moveTo(x, 0);
                ctx.lineTo(x, canvas.height);
                ctx.stroke();

                ctx.fillStyle = '#7f849c';
                ctx.fillText(t.toFixed(1) + ' ms', x + 4, 14);
            }

            flatFilteredItems.forEach(item => {
                const x = item.Start * xScale + panX;
                const y = 30 + item.Depth * rowHeight + panY;
                const w = item.Elapsed * xScale;
                const h = rowHeight - padding;

                if (x + w < 0 || x > canvas.width) return;

                let baseColor = 'rgba(88, 91, 112, 0.7)';
                if (item.Type === 'Function') baseColor = 'rgba(137, 180, 250, 0.8)';
                else if (item.Type === 'Class') baseColor = 'rgba(203, 166, 247, 0.8)';
                else if (item.Type === 'Event') baseColor = 'rgba(250, 179, 135, 0.8)';

                if (item.Elapsed > 50) baseColor = 'rgba(243, 139, 168, 0.85)';

                ctx.fillStyle = baseColor;
                ctx.fillRect(x, y, w, h);

                if (item === hoveredItem) {
                    ctx.strokeStyle = '#ffffff';
                    ctx.lineWidth = 1.5;
                    ctx.strokeRect(x + 0.5, y + 0.5, w - 1, h - 1);
                }

                if (item === selectedItem) {
                    ctx.strokeStyle = '#a6e3a1';
                    ctx.lineWidth = 2;
                    ctx.strokeRect(x + 0.5, y + 0.5, w - 1, h - 1);
                }

                if (w > 30) {
                    ctx.fillStyle = '#11111b';
                    ctx.font = '11px var(--font-sans)';
                    ctx.textBaseline = 'middle';
                    const text = item.Name + ' (' + item.Elapsed.toFixed(2) + ' ms)';
                    ctx.fillText(truncateText(ctx, text, w - 8), x + 5, y + h / 2);
                }
            });
        }

        function truncateText(context, text, maxWidth) {
            let width = context.measureText(text).width;
            if (width <= maxWidth) return text;

            let length = text.length;
            while (width > maxWidth && length > 0) {
                length--;
                text = text.substring(0, length) + '...';
                width = context.measureText(text).width;
            }
            return text;
        }

        function getItemAtPosition(x, y) {
            for (let i = flatFilteredItems.length - 1; i >= 0; i--) {
                const item = flatFilteredItems[i];
                const itemX = item.Start * xScale + panX;
                const itemY = 30 + item.Depth * rowHeight + panY;
                const itemW = item.Elapsed * xScale;
                const itemH = rowHeight - padding;

                const clickableWidth = Math.max(itemW, 2);

                if (x >= itemX && x <= itemX + clickableWidth && y >= itemY && y <= itemY + itemH) {
                    return item;
                }
            }
            return null;
        }

        function updateTooltip(mouseX, mouseY) {
            const tooltip = document.getElementById('flame-tooltip');
            if (hoveredItem) {
                tooltip.innerHTML = `
                    <div class='tooltip-title'>${hoveredItem.Name}</div>
                    <div class='tooltip-row'>
                        <span class='tooltip-lbl'>Type</span>
                        <span class='tooltip-val' style='color: ${getColorForType(hoveredItem.Type)}'>${hoveredItem.Type}</span>
                    </div>
                    <div class='tooltip-row'>
                        <span class='tooltip-lbl'>Duration</span>
                        <span class='tooltip-val' style='color: var(--accent-green)'>${hoveredItem.Elapsed.toFixed(3)} ms</span>
                    </div>
                    <div class='tooltip-row'>
                        <span class='tooltip-lbl'>Start Time</span>
                        <span class='tooltip-val'>${hoveredItem.Start.toFixed(3)} ms</span>
                    </div>
                    ${hoveredItem.Timestamp ? `
                    <div class='tooltip-row'>
                        <span class='tooltip-lbl'>Timestamp</span>
                        <span class='tooltip-val'>${hoveredItem.Timestamp}</span>
                    </div>` : ''}
                    <div class='tooltip-row'>
                        <span class='tooltip-lbl'>File</span>
                        <span class='tooltip-val'>${hoveredItem.File}</span>
                    </div>
                    ${hoveredItem.Line ? `
                    <div class='tooltip-row'>
                        <span class='tooltip-lbl'>Line Number</span>
                        <span class='tooltip-val'>${hoveredItem.Line}</span>
                    </div>` : ''}
                `;
                tooltip.style.left = (mouseX + 15) + 'px';
                tooltip.style.top = (mouseY + 15) + 'px';
                tooltip.style.display = 'block';
            } else {
                tooltip.style.display = 'none';
            }
        }

        function getColorForType(type) {
            if (type === 'Function') return '#89b4fa';
            if (type === 'Class') return '#cba6f7';
            if (type === 'Event') return '#fab387';
            return '#585b70';
        }

        function zoomFlame(factor) {
            xScale *= factor;
            drawFlamegraph();
        }

        function resetZoom() {
            xScale = 1.0;
            panX = 0;
            panY = 0;
            drawFlamegraph();
        }

        function fitWidth() {
            const width = document.getElementById('canvas-container').clientWidth;
            if (totalTime > 0) {
                xScale = (width - 40) / totalTime;
                panX = 20;
                panY = 0;
                drawFlamegraph();
            }
        }

        function selectItem(item) {
            selectedItem = item;
            drawFlamegraph();

            const container = document.getElementById('details-container');
            let paramsHtml = '';
            if (item.Params && item.Params.length > 0) {
                paramsHtml = '<div class=\'detail-row\'><span class=\'detail-lbl\'>Parameters</span><div class=\'detail-val\' style=\'white-space: pre;\'>';
                for (let i = 0; i < item.Params.length; i += 2) {
                    paramsHtml += `<b>${item.Params[i]}:</b> ${escapeHtml(item.Params[i+1])}\n`;
                }
                paramsHtml += '</div></div>';
            }

            container.innerHTML = `
                <div class='detail-row'>
                    <span class='detail-lbl'>Name</span>
                    <span class='detail-val' style='color: var(--accent); font-weight: bold;'>${item.Name}</span>
                </div>
                <div class='detail-row'>
                    <span class='detail-lbl'>Category / Type</span>
                    <span class='detail-val' style='color: ${getColorForType(item.Type)};'>${item.Type}</span>
                </div>
                <div class='detail-row'>
                    <span class='detail-lbl'>Execution Duration</span>
                    <span class='detail-val' style='color: var(--accent-green); font-weight: bold;'>${item.Elapsed.toFixed(3)} ms</span>
                </div>
                ${item.Timestamp ? `
                <div class='detail-row'>
                    <span class='detail-lbl'>Timestamp</span>
                    <span class='detail-val'>${item.Timestamp}</span>
                </div>` : ''}
                <div class='detail-row'>
                    <span class='detail-lbl'>Relative Start</span>
                    <span class='detail-val'>${item.Start.toFixed(3)} ms</span>
                </div>
                <div class='detail-row'>
                    <span class='detail-lbl'>Definition File</span>
                    <span class='detail-val'>${item.File} ${item.Line ? '(Line ' + item.Line + ')' : ''}</span>
                </div>
                ${item.Code ? `
                <div class='detail-row'>
                    <span class='detail-lbl'>Emitted AHK Statement</span>
                    <pre class='detail-val' style='white-space: pre-wrap; overflow-x: auto;'>${escapeHtml(item.Code)}</pre>
                </div>` : ''}
                ${item.Error ? `
                <div class='detail-row'>
                    <span class='detail-lbl' style='color: var(--accent-red)'>❌ Catch Exception</span>
                    <span class='detail-val' style='color: var(--accent-red); background-color: rgba(243, 139, 168, 0.1); border-color: rgba(243, 139, 168, 0.3); font-weight: bold;'>${escapeHtml(item.Error)}</span>
                </div>` : ''}
                ${paramsHtml}
            `;
        }

        function escapeHtml(text) {
            if (!text) return '';
            return text.toString()
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(new RegExp(String.fromCharCode(34), 'g'), '&quot;')
                .replace(/'/g, '&#039;');
        }

        function renderTree() {
            const container = document.getElementById('tree-root-container');
            container.innerHTML = '';

            if (filteredRoots.length === 0) {
                container.innerHTML = '<div class=\'empty-state\'>No matching items in tree view.</div>';
                return;
            }

            filteredRoots.forEach(root => {
                container.appendChild(createTreeNodeElement(root));
            });
        }

        function createTreeNodeElement(item) {
            const nodeEl = document.createElement('div');
            nodeEl.className = 'tree-node';
            nodeEl.style.marginLeft = item.Parent ? '18px' : '0px';

            const headerEl = document.createElement('div');
            headerEl.className = 'tree-node-header';

            const toggleEl = document.createElement('span');
            toggleEl.className = 'tree-toggle';
            toggleEl.innerText = item.Children.length > 0 ? '▶' : '•';

            const contentEl = document.createElement('div');
            contentEl.className = 'tree-node-content';

            const nameSpan = document.createElement('span');
            nameSpan.style.color = getColorForType(item.Type);
            nameSpan.innerText = item.Name;

            const timingSpan = document.createElement('span');
            timingSpan.className = 'tree-timing';
            timingSpan.innerText = item.Elapsed.toFixed(2) + ' ms';

            const fileSpan = document.createElement('span');
            fileSpan.className = 'tree-file';
            fileSpan.innerText = '[' + item.File + ']';

            const barOuter = document.createElement('span');
            barOuter.className = 'tree-bar-outer';
            const pct = totalTime > 0 ? (item.Elapsed / totalTime) * 100 : 0;
            const barInner = document.createElement('span');
            barInner.className = 'tree-bar-inner';
            barInner.style.width = Math.min(pct, 100) + '%';
            barOuter.appendChild(barInner);

            contentEl.appendChild(nameSpan);
            contentEl.appendChild(timingSpan);
            contentEl.appendChild(barOuter);
            contentEl.appendChild(fileSpan);

            headerEl.appendChild(toggleEl);
            headerEl.appendChild(contentEl);
            nodeEl.appendChild(headerEl);

            const childrenContainer = document.createElement('div');
            childrenContainer.style.display = 'none';
            nodeEl.appendChild(childrenContainer);

            headerEl.addEventListener('click', (e) => {
                e.stopPropagation();
                selectItem(item);

                if (item.Children.length > 0) {
                    if (childrenContainer.style.display === 'none') {
                        childrenContainer.style.display = 'block';
                        toggleEl.innerText = '▼';
                        if (childrenContainer.children.length === 0) {
                            item.Children.forEach(child => {
                                childrenContainer.appendChild(createTreeNodeElement(child));
                            });
                        }
                    } else {
                        childrenContainer.style.display = 'none';
                        toggleEl.innerText = '▶';
                    }
                }
            });

            return nodeEl;
        }

        let hotspotsData = [];

        function renderHotspots() {
            const tbody = document.getElementById('hotspots-tbody');
            tbody.innerHTML = '';

            const aggregated = {};
            flatFilteredItems.forEach(item => {
                const name = item.Name;
                if (!aggregated[name]) {
                    aggregated[name] = {
                        name: name,
                        type: item.Type,
                        count: 0,
                        cumulative: 0.0,
                        self: 0.0,
                        max: 0.0,
                        avg: 0.0
                    };
                }
                const entry = aggregated[name];
                entry.count++;
                entry.cumulative += item.Elapsed;
                if (item.Elapsed > entry.max) entry.max = item.Elapsed;

                let childrenTime = 0.0;
                item.Children.forEach(c => childrenTime += c.Elapsed);
                entry.self += Math.max(0, item.Elapsed - childrenTime);
            });

            hotspotsData = Object.values(aggregated);
            hotspotsData.forEach(entry => {
                entry.avg = entry.cumulative / entry.count;
            });

            sortHotspotsData();

            if (hotspotsData.length === 0) {
                tbody.innerHTML = '<tr><td colspan=\'5\' class=\'empty-state\'>No matching trace items for hotspots.</td></tr>';
                return;
            }

            hotspotsData.forEach(entry => {
                const tr = document.createElement('tr');
                tr.innerHTML = `
                    <td class='tbl-name' style='color: ${getColorForType(entry.type)}'>${entry.name}</td>
                    <td>${entry.count.toLocaleString()}</td>
                    <td style='color: var(--accent-green); font-weight: 600;'>${entry.cumulative.toFixed(2)} ms</td>
                    <td>${entry.self.toFixed(2)} ms</td>
                    <td>${entry.avg.toFixed(2)} ms</td>
                `;
                tbody.appendChild(tr);
            });
        }

        function sortHotspots(colName) {
            if (currentSort.column === colName) {
                currentSort.desc = !currentSort.desc;
            } else {
                currentSort.column = colName;
                currentSort.desc = true;
            }
            renderHotspots();
        }

        function sortHotspotsData() {
            const col = currentSort.column;
            const desc = currentSort.desc;

            hotspotsData.sort((a, b) => {
                let valA = a[col];
                let valB = b[col];

                if (typeof valA === 'string') {
                    return desc ? valB.localeCompare(valA) : valA.localeCompare(valB);
                } else {
                    return desc ? valB - valA : valA - valB;
                }
            });
        }

        function renderAll() {
            resizeCanvas();
            drawFlamegraph();
            renderTree();
            renderHotspots();
        }
    </script>
</body>
</html>
";
    }
}
