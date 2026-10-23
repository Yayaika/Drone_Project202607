using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>Timeline 對話片段：一句台詞 (說話者、內容、頭像在左或右)，片段長度即顯示時間</summary>
[System.Serializable]
public class DialogueClip : PlayableAsset, ITimelineClipAsset
{
    public string speaker;
    [Tooltip("頭像上顯示的字；留空則用說話者名字的第一個字")]
    public string portrait;
    [TextArea(2, 4)] public string line;
    [Tooltip("頭像與名牌顯示在對話框右側")]
    public bool rightSide;

    public ClipCaps clipCaps => ClipCaps.None;

    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
    {
        ScriptPlayable<DialogueBehaviour> playable = ScriptPlayable<DialogueBehaviour>.Create(graph);
        DialogueBehaviour behaviour = playable.GetBehaviour();
        behaviour.Speaker = speaker;
        behaviour.Portrait = string.IsNullOrEmpty(portrait) && !string.IsNullOrEmpty(speaker) ? speaker.Substring(0, 1) : portrait;
        behaviour.Line = line;
        behaviour.RightSide = rightSide;
        return playable;
    }
}

public class DialogueBehaviour : PlayableBehaviour
{
    public string Speaker;
    public string Portrait;
    public string Line;
    public bool RightSide;
}

/// <summary>每幀找出目前作用中的對話片段交給 <see cref="IntroCutscene"/> 顯示；片段之間的空檔隱藏字幕</summary>
public class DialogueMixerBehaviour : PlayableBehaviour
{
    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        IntroCutscene target = playerData as IntroCutscene;
        if (target == null) return;

        for (int i = 0; i < playable.GetInputCount(); i++)
        {
            if (playable.GetInputWeight(i) <= 0f) continue;
            ScriptPlayable<DialogueBehaviour> input = (ScriptPlayable<DialogueBehaviour>)playable.GetInput(i);
            DialogueBehaviour dialogue = input.GetBehaviour();
            target.ShowLine(dialogue.Speaker, dialogue.Portrait, dialogue.Line, dialogue.RightSide, (float)input.GetTime());
            return;
        }
        target.HideLine();
    }
}
