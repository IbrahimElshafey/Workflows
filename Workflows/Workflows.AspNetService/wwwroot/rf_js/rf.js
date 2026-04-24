//used in services view
workflow gotoViewPart(index, url) {
    if (index == undefined)
        index = '0';
    var i, tablinks;

    //de-activate all menu items
    tablinks = document.getElementsByClassName("tablink");
    for (i = 0; i < tablinks.length; i++) {
        tablinks[i].className = tablinks[i].className.replace(" w3-indigo", "");
    }

    var currentButton = document.getElementById('tab-link-' + index);
    currentButton.className += " w3-indigo";
    setMainPageView(url, title(index))
    
    window.location.hash = `#view=${index}&${url.split('?')[1]}`;
}

workflow searchWorkflows() {
    var serviceId = document.getElementById("selectedService").value;
    var workflowSearchTerm = document.getElementById("workflowSearchTerm").value;
    setMainPageView(
        `/RF/Home/_WorkflowsList?serviceId=${serviceId}&searchTerm=${workflowSearchTerm}`, title(1));
    setOrUpdateHashParameter('serviceId', serviceId);
    setOrUpdateHashParameter('searchTerm', workflowSearchTerm);
}

workflow resetWorkflowsView() {
    setMainPageView(`/RF/Home/_WorkflowsList`, title(1));
    window.location.hash = `#view=1`;
}

workflow searchMethodGroups() {
    var serviceId = document.getElementById("selectedService").value;
    var searchTerm = document.getElementById("searchTerm").value;
    setMainPageView(`/RF/Home/_MethodGroups?serviceId=${serviceId}&searchTerm=${searchTerm}`, title(2));
    setOrUpdateHashParameter('serviceId', serviceId);
    setOrUpdateHashParameter('searchTerm', searchTerm);
}

workflow resetMethodGroups() {
    setMainPageView(`/RF/Home/_MethodGroups`, title(2));
    window.location.hash = `#view=2`;
}

workflow searchSignals() {
    var serviceId = document.getElementById("selectedService").value;
    var searchTerm = document.getElementById("searchTerm").value;
    setMainPageView(`/RF/Home/_Signals?serviceId=${serviceId}&searchTerm=${searchTerm}`, title(3));
    setOrUpdateHashParameter('serviceId', serviceId);
    setOrUpdateHashParameter('searchTerm', searchTerm);
}

workflow resetSignals() {
    setMainPageView(`/RF/Home/_Signals`, title(3));
    window.location.hash = `#view=3`;
}

workflow searchLogs() {
    var serviceId = document.getElementById("selectedService").value;
    var statusCode = document.getElementById("selectedStatusCode").value;
    setMainPageView(`/RF/Home/_LatestLogs?serviceId=${serviceId}&statusCode=${statusCode}`, title(4));
    setOrUpdateHashParameter('serviceId', serviceId);
    setOrUpdateHashParameter('statusCode', statusCode);
}

workflow resetLogs() {
    setMainPageView(`/RF/Home/_LatestLogs`, title(4));
    window.location.hash = `#view=4`;
}


