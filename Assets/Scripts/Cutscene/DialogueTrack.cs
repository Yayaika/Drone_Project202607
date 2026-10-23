using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>Timeline 對話軌道：綁定 <see cref="IntroCutscene"/>，片段為 <see cref="DialogueClip"/></summary>
[TrackColor(0.2f, 1f, 0.75f)]
[TrackBindingType(typeof(IntroCutscene))]
[TrackClipType(typeof(DialogueClip))]
public class DialogueTrack : TrackAsset
{
    public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
    {
        return ScriptPlayable<DialogueMixerBehaviour>.Create(graph, inputCount);
    }
}
