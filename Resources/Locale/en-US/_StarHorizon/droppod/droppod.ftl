ent-StarHorizonDropPod = drop pod
    .desc = A bluespace drop pod. Load it with cargo or climb in yourself, set the destination at a drop pod console and launch.
ent-StarHorizonDropPodMarker = drop pod landing zone
    .desc = Something is about to fall from the sky here.
ent-StarHorizonDropPodSmoke = dust cloud
ent-StarHorizonDropPodConsole = drop pod console
    .desc = Sets the landing coordinates and launches the nearest drop pod.
ent-StarHorizonDropPodConsoleCircuitboard = drop pod console board
    .desc = A computer printed circuit board for a drop pod console.

droppod-fail-open = Launch aborted: the pod door is open!
droppod-error-no-pod = No drop pod nearby.
droppod-error-bad-target = Invalid target coordinates.

droppod-ui-title = Drop pod console
droppod-ui-no-pod = No drop pod found (6 tile range).
droppod-ui-state = State: { $state }
droppod-ui-occupants = Objects inside: { $count }
droppod-ui-timer = Remaining: { $seconds } s
droppod-ui-target = Landing coordinates (world):
droppod-ui-here = Use console coordinates
droppod-ui-launch = Launch
droppod-ui-abort = Abort

droppod-state-Idle = idle
droppod-state-Countdown = countdown, close the pod door
droppod-state-ZoomingOut = launching
droppod-state-InBluespace = in bluespace
droppod-state-Landing = landing
