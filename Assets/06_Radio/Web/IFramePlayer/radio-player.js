(function () {
    "use strict";

    var player;
    var pendingVideoId = "";
    var pendingAutoplay = false;
    var pendingVolume = 80;
    var isPlayerReady = false;
    var isDragging = false;
    var lastDragX = 0;
    var lastDragY = 0;

    var dragHandle = document.getElementById("drag-handle");
    var titleElement = document.getElementById("track-title");
    var statusElement = document.getElementById("player-status");

    function setStatus(message) {
        if (statusElement) {
            statusElement.textContent = message;
        }

        console.log("[GreenLightRadio] " + message);
    }

    function setTitle(title) {
        if (titleElement) {
            titleElement.textContent = title || "Untitled radio slot";
        }
    }

    function notifyUnity(eventName, payload) {
        var message = JSON.stringify({
            eventName: eventName,
            payload: payload || {}
        });

        if (window.Unity && typeof window.Unity.call === "function") {
            window.Unity.call(message);
            return;
        }

        if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.unityControl) {
            window.webkit.messageHandlers.unityControl.postMessage(message);
            return;
        }

        console.log("[GreenLightRadioBridge] " + message);
    }

    function bindDragHandle() {
        if (!dragHandle || !window.PointerEvent) {
            return;
        }

        dragHandle.addEventListener("pointerdown", onDragStart);
        dragHandle.addEventListener("pointermove", onDragMove);
        dragHandle.addEventListener("pointerup", onDragEnd);
        dragHandle.addEventListener("pointercancel", onDragEnd);
    }

    function onDragStart(event) {
        isDragging = true;
        lastDragX = event.clientX;
        lastDragY = event.clientY;
        dragHandle.setPointerCapture(event.pointerId);
        notifyUnity("dragStart");
        event.preventDefault();
    }

    function onDragMove(event) {
        if (!isDragging) {
            return;
        }

        var dx = event.clientX - lastDragX;
        var dy = event.clientY - lastDragY;
        lastDragX = event.clientX;
        lastDragY = event.clientY;

        if (dx !== 0 || dy !== 0) {
            notifyUnity("dragDelta", { dx: dx, dy: dy });
        }

        event.preventDefault();
    }

    function onDragEnd(event) {
        if (!isDragging) {
            return;
        }

        isDragging = false;
        notifyUnity("dragEnd");
        event.preventDefault();
    }

    function loadVideo(videoId, displayName, autoplay) {
        if (!videoId || typeof videoId !== "string") {
            setStatus("Load skipped. videoId is empty.");
            return false;
        }

        pendingVideoId = cleanVideoId(videoId);
        pendingAutoplay = autoplay !== false;
        setTitle(displayName || pendingVideoId);

        if (!isPlayerReady || !player) {
            setStatus("Queued video: " + pendingVideoId);
            return true;
        }

        player.loadVideoById(pendingVideoId);
        player.setVolume(pendingVolume);

        if (pendingAutoplay) {
            player.playVideo();
        } else {
            player.pauseVideo();
        }

        setStatus("Loaded video: " + pendingVideoId);
        notifyUnity("loaded", { videoId: pendingVideoId });
        return true;
    }

    function play() {
        if (!player || !isPlayerReady) {
            setStatus("Play queued until player is ready.");
            return false;
        }

        player.playVideo();
        setStatus("Play requested.");
        return true;
    }

    function pause() {
        if (!player || !isPlayerReady) {
            setStatus("Pause skipped. Player is not ready.");
            return false;
        }

        player.pauseVideo();
        setStatus("Pause requested.");
        return true;
    }

    function setVolume(volume) {
        var nextVolume = Number(volume);

        if (!Number.isFinite(nextVolume)) {
            return false;
        }

        pendingVolume = Math.max(0, Math.min(100, Math.round(nextVolume)));

        if (player && isPlayerReady) {
            player.setVolume(pendingVolume);
        }

        setStatus("Volume set to " + pendingVolume + ".");
        return true;
    }

    function onPlayerReady(event) {
        isPlayerReady = true;
        event.target.setVolume(pendingVolume);
        setStatus("IFrame Player is ready.");
        notifyUnity("ready");

        if (pendingVideoId) {
            loadVideo(pendingVideoId, titleElement ? titleElement.textContent : pendingVideoId, pendingAutoplay);
        }
    }

    function onPlayerStateChange(event) {
        notifyUnity("stateChanged", { state: event.data });
    }

    function onPlayerError(event) {
        setStatus("YouTube player error: " + event.data);
        notifyUnity("error", { code: event.data, videoId: pendingVideoId });
    }

    function cleanVideoId(value) {
        return value.trim().split(/[?&#]/)[0];
    }

    window.onYouTubeIframeAPIReady = function () {
        player = new YT.Player("youtube-player", {
            width: "100%",
            height: "100%",
            playerVars: {
                autoplay: 0,
                controls: 1,
                modestbranding: 1,
                rel: 0,
                playsinline: 1,
                enablejsapi: 1,
                origin: window.location.origin
            },
            events: {
                onReady: onPlayerReady,
                onStateChange: onPlayerStateChange,
                onError: onPlayerError
            }
        });
    };

    window.GreenLightRadio = {
        loadVideo: loadVideo,
        play: play,
        pause: pause,
        setVolume: setVolume
    };

    bindDragHandle();
}());
