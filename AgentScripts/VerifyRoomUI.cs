using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using PokeChess.Client.UI;
using PokeChess.Network.Session;
public static class VerifyRoomUI
{
    public static string Main(){Run();return "UI button checks started";}
    private static async Task Wait(Func<bool> condition){var end=Time.realtimeSinceStartupAsDouble+45;while(!condition()){if(Time.realtimeSinceStartupAsDouble>end)throw new TimeoutException();await Task.Yield();}}
    private static async void Run(){try{
        var view=UnityEngine.Object.FindFirstObjectByType<RoomFlowView>();var panel=view.GetComponentInChildren<Canvas>().transform.Find("RoomPanel");panel.gameObject.SetActive(true);var browse=panel.Find("Browse");var s=OnlineConnection.Instance;
        var name=browse.Find("RoomName").GetComponent<InputField>();name.text="";browse.Find("Create room").GetComponent<Button>().onClick.Invoke();await Task.Yield();if(s.Error!="InvalidRoomName"||s.SessionId!=null)throw new Exception("Blank name accepted");
        name.text="UI QA <b>literal</b>";browse.Find("Max players: 8").GetComponent<Button>().onClick.Invoke();browse.Find("Visibility: Public").GetComponent<Button>().onClick.Invoke();browse.Find("Create room").GetComponent<Button>().onClick.Invoke();await Wait(()=>s.State==ConnectionState.Connected);await Task.Yield();
        if(s.Room.Capacity!=2||!s.Room.IsPrivate)throw new Exception("UI options ignored");
        var title=panel.Find("Lobby/RoomTitle").GetComponent<Text>();if(title.supportRichText||!title.text.Contains(name.text))throw new Exception("Room title altered");
        panel.Find("Lobby/Copy code").GetComponent<Button>().onClick.Invoke();if(GUIUtility.systemCopyBuffer!=s.Code)throw new Exception("Clipboard code mismatch");
        panel.Find("Lobby/Leave room").GetComponent<Button>().onClick.Invoke();await Wait(()=>s.State==ConnectionState.Idle&&!s.IsBusy);await Task.Yield();if(!browse.gameObject.activeSelf)throw new Exception("Browse did not resume");
        foreach(var cap in new[]{1,9}){try{new RoomCreation("QA",cap,false).Validate();throw new Exception("Bad capacity accepted");}catch(ArgumentException){}}
        File.WriteAllText("TestResults/WBS5.3/ui-actions.json","{\"status\":\"Passed\",\"blankNameRejected\":true,\"capacityVisibilityApplied\":true,\"clipboard\":true,\"literalRoomName\":true,\"leaveToBrowser\":true}");
    }catch(Exception e){File.WriteAllText("TestResults/WBS5.3/ui-actions.json",JsonUtility.ToJson(new Failure{error=e.ToString()}));}}
    [Serializable]private sealed class Failure{public string error;}
}
