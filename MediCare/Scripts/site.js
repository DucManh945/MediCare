/* MediCare - Custom JavaScript */
(function ($) {
    'use strict';

    $(function () {
        initScrollToTop();
        initStickyNav();
        initFadeInAnimations();
        initAppointmentDate();
        initTogglePassword();
        initSmoothScroll();
        initNavbarActive();
        initSidebarToggle();
    });

    function initScrollToTop() {
        var $btn = $('#scrollToTop');
        if ($btn.length === 0) {
            $btn = $('<button/>', {
                'id': 'scrollToTop',
                'class': 'btn-scroll-top',
                'aria-label': 'Lên đầu trang'
            }).html('<i class="fas fa-chevron-up"></i>').appendTo('body');
        }

        $(window).on('scroll', function () {
            if ($(this).scrollTop() > 400) {
                $btn.addClass('visible');
            } else {
                $btn.removeClass('visible');
            }
        });

        $(document).on('click', '#scrollToTop', function (e) {
            e.preventDefault();
            $('html, body').animate({ scrollTop: 0 }, 500, 'swing');
        });
    }

    function initStickyNav() {
        var $nav = $('.navbar-main');
        if ($nav.length === 0) return;

        var navOffset = $nav.offset().top + 60;
        $(window).on('scroll', function () {
            if ($(this).scrollTop() > navOffset) {
                $nav.css('box-shadow', '0 10px 25px -5px rgba(15, 23, 42, 0.1)');
            } else {
                $nav.css('box-shadow', 'var(--mc-shadow, 0 4px 6px -1px rgba(15,23,42,0.08))');
            }
        });
    }

    function initFadeInAnimations() {
        var $els = $('.service-card, .doctor-card, .testimonial-card, .stat-card, .info-card, .section-title');
        $els.each(function () {
            $(this).addClass('fade-in');
        });

        function checkFade() {
            var winH = $(window).height();
            var scrollTop = $(window).scrollTop();
            $els.each(function () {
                var $el = $(this);
                if ($el.hasClass('visible')) return;
                var top = $el.offset().top;
                if (top < scrollTop + winH - 80) {
                    $el.addClass('visible');
                }
            });
        }

        $(window).on('scroll', checkFade);
        setTimeout(checkFade, 200);
    }

    function initAppointmentDate() {
        var $dateInput = $('#AppointmentDate');
        if ($dateInput.length === 0) return;

        var today = new Date();
        var dd = String(today.getDate()).padStart(2, '0');
        var mm = String(today.getMonth() + 1).padStart(2, '0');
        var yyyy = today.getFullYear();
        var minDate = yyyy + '-' + mm + '-' + dd;
        $dateInput.attr('min', minDate);
    }

    function initTogglePassword() {
        $(document).on('click', '#togglePw', function () {
            var $pw = $('#Password');
            var $icon = $(this).find('i');
            if ($pw.attr('type') === 'password') {
                $pw.attr('type', 'text');
                $icon.removeClass('fa-eye').addClass('fa-eye-slash');
            } else {
                $pw.attr('type', 'password');
                $icon.removeClass('fa-eye-slash').addClass('fa-eye');
            }
        });
    }

    function initSmoothScroll() {
        $('a[href^="#"]:not([href="#"])').on('click', function (e) {
            var target = $(this.getAttribute('href'));
            if (target.length) {
                e.preventDefault();
                var offset = target.offset().top - 100;
                $('html, body').stop().animate({ scrollTop: offset }, 600, 'swing');
            }
        });
    }

    function initNavbarActive() {
        var current = window.location.pathname;
        $('.navbar-main .nav-link').each(function () {
            var href = $(this).attr('href');
            if (!href) return;
            if (href === '/' && current !== '/') return;
            if (current.indexOf(href) !== -1 || href === current) {
                $(this).addClass('active');
                $(this).css('background', 'var(--mc-primary-light)');
                $(this).css('color', 'var(--mc-primary) !important');
            }
        });
    }

    function initSidebarToggle() {
        var $sidebar = $('.dashboard-sidebar');
        if ($sidebar.length === 0) return;

        $(document).on('click', '.sidebar-toggle', function (e) {
            e.preventDefault();
            e.stopPropagation();
            $sidebar.toggleClass('show');
        });

        $(document).on('click', function (e) {
            if ($(window).width() >= 992) return;
            if (!$sidebar.hasClass('show')) return;
            if ($(e.target).closest('.dashboard-sidebar').length || $(e.target).closest('.sidebar-toggle').length) return;
            $sidebar.removeClass('show');
        });

        $(document).on('click', '.dashboard-sidebar .sidebar-link', function () {
            if ($(window).width() < 992) {
                $sidebar.removeClass('show');
            }
        });
    }

    window.MediCare = {
        toast: function (message, type) {
            type = type || 'success';
            var $toast = $('<div/>', {
                'class': 'alert alert-' + type + ' alert-dismissible fade show',
                'role': 'alert',
                'css': {
                    'position': 'fixed',
                    'top': '100px',
                    'right': '2rem',
                    'z-index': '9999',
                    'min-width': '280px',
                    'box-shadow': '0 10px 25px rgba(0,0,0,0.15)',
                    'border': 'none',
                    'border-radius': '12px'
                }
            }).html(message + '<button type="button" class="btn-close" data-bs-dismiss="alert"></button>');
            $('body').append($toast);
            setTimeout(function () {
                $toast.alert('close');
            }, 4000);
        }
    };

})(jQuery);
