export function renderMermaid(elementId, definition) {
    if (window.mermaid) {
        window.mermaid.render('mermaid-svg-' + elementId, definition).then(({ svg }) => {
            const element = document.getElementById(elementId);
            if (element) {
                element.innerHTML = svg;
            }
        });
    }
}

export function scrollToBottom(elementId) {
    const element = document.getElementById(elementId);
    if (element) {
        element.scrollTop = element.scrollHeight;
    }
}

export function render3DPlot(elementId, x, y, z, text) {
    const element = document.getElementById(elementId);
    if (!element || !window.Plotly) return;

    // We only have one point for the detail view, but we'll structure it as a scatter3d
    // To make it look "enterprise", we might want to plot the domain context if we had it.
    // For now, we plot the single point with a nice glow.

    const data = [{
        x: [x],
        y: [y],
        z: [z],
        mode: 'markers+text',
        marker: {
            size: 12,
            color: '#0d6efd',
            line: {
                color: 'white',
                width: 2
            },
            opacity: 0.8
        },
        text: [text],
        textposition: 'top center',
        textfont: {
            color: 'white',
            family: 'Helvetica Neue, sans-serif'
        },
        type: 'scatter3d'
    }];

    // Generate a wireframe sphere to represent S3 projection unit hypersphere context (3D slice)
    // S3 is x^2 + y^2 + z^2 + w^2 = 1. We project to 3D.
    // We can visualize the unit sphere as a reference context.
    
    // Create sphere wireframe
    const sphere_u = [];
    const sphere_v = [];
    const sphere_x = [];
    const sphere_y = [];
    const sphere_z = [];
    
    for (let i = 0; i < 20; i++) {
        const phi = (i * Math.PI) / 19;
        for (let j = 0; j < 20; j++) {
            const theta = (j * 2 * Math.PI) / 19;
            sphere_x.push(Math.sin(phi) * Math.cos(theta));
            sphere_y.push(Math.sin(phi) * Math.sin(theta));
            sphere_z.push(Math.cos(phi));
        }
    }

    const sphereMesh = {
        x: sphere_x,
        y: sphere_y,
        z: sphere_z,
        mode: 'lines',
        type: 'mesh3d',
        opacity: 0.1,
        color: '#333',
        alphahull: 0
    };

    const layout = {
        margin: { l: 0, r: 0, b: 0, t: 0 },
        paper_bgcolor: 'rgba(0,0,0,0)',
        plot_bgcolor: 'rgba(0,0,0,0)',
        showlegend: false,
        scene: {
            xaxis: { title: '', showgrid: true, zeroline: false, showline: false, ticklabels: false, gridcolor: '#333' },
            yaxis: { title: '', showgrid: true, zeroline: false, showline: false, ticklabels: false, gridcolor: '#333' },
            zaxis: { title: '', showgrid: true, zeroline: false, showline: false, ticklabels: false, gridcolor: '#333' },
            camera: {
                eye: { x: 1.5, y: 1.5, z: 1.5 }
            }
        },
        height: 400
    };

    const config = { responsive: true, displayModeBar: false };

    Plotly.newPlot(elementId, [sphereMesh, ...data], layout, config);
}