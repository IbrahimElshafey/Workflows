// Workflows Admin UI - General JavaScript

(function () {
    'use strict';

    // Auto-dismiss alerts after 5 seconds
    document.addEventListener('DOMContentLoaded', function () {
        var alerts = document.querySelectorAll('.alert-dismissible');
        alerts.forEach(function (alert) {
            setTimeout(function () {
                var closeButton = alert.querySelector('.btn-close');
                if (closeButton) {
                    closeButton.click();
                }
            }, 5000);
        });
    });
})();
