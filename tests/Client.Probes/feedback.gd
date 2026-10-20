extends SceneTree
var failures=[]
func check(ok,message):
    if not ok:failures.append(message)
func _initialize():call_deferred("run")
func run():
    var audio=load("res://FeedbackAudio.cs").new()
    root.add_child(audio)
    await process_frame
    var players=audio.get_children()
    check(players.size()==4,"Audio pool must have two reusable effects players and two loops")
    audio.call("SetActive",true,true)
    await create_timer(.08).timeout
    var loops=players.filter(func(p):return p.stream is AudioStreamWAV and p.stream.loop_mode==AudioStreamWAV.LOOP_FORWARD)
    check(loops.size()==2,"Ambience/music WAV loops were not imported")
    if loops.size()==2:
        check(abs(loops[0].stream.get_length()-8)<.01,"Ambience duration")
        check(abs(loops[1].stream.get_length()-16)<.01,"Music duration")
    audio.call("Click")
    check(players.any(func(p):return p.playing and not p.stream_paused),"No actual playback")
    audio.call("SetActive",false,false)
    check(players.all(func(p):return not p.playing or p.stream_paused),"Focus/pause left audio running")
    audio.call("SetActive",true,true)
    await process_frame
    check(loops.size()==2 and loops[0].playing and not loops[0].stream_paused,"Ambience did not resume")
    check(loops.size()==2 and (loops[1].stream_paused or not loops[1].playing),"Optional music must be muted by default")
    audio.queue_free()
    players.clear()
    loops.clear()
    await process_frame
    await process_frame
    print("DELSK_FEEDBACK "+JSON.stringify({"failures":failures,"scope":"Godot playback state/Dummy driver; no speaker/device claim"}))
    quit(0 if failures.is_empty() else 1)
