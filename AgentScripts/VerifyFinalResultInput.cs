using System;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using PokeChess.Client.UI;
public static class VerifyFinalResultInput {
    public static string Main() {
        var v=UnityEngine.Object.FindFirstObjectByType<PlacementSandboxView>();
        if(!v.FinalResultVisible)throw new Exception("Final result required");
        var pointer=new PointerEventData(EventSystem.current);
        pointer.position=RectTransformUtility.WorldToScreenPoint(null,v.SurrenderButton.transform.position);
        var hits=new System.Collections.Generic.List<RaycastResult>();EventSystem.current.RaycastAll(pointer,hits);
        if(hits.Count==0||hits[0].gameObject.name!="FinalBackdrop")throw new Exception("Underlying input: "+string.Join(",",hits.Select(h=>h.gameObject.name)));
        pointer.position=RectTransformUtility.WorldToScreenPoint(null,v.NewMatchButton.transform.position);
        hits.Clear();EventSystem.current.RaycastAll(pointer,hits);
        if(hits.Count==0||hits[0].gameObject!=v.NewMatchButton.gameObject)throw new Exception("New match input blocked");
        return "Final backdrop blocks underlying controls; New match remains clickable.";
    }
}
