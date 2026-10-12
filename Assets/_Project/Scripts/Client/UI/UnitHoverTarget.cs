using UnityEngine;
using UnityEngine.EventSystems;

namespace PokeChess.Client.UI
{
    public sealed class UnitHoverTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public PlacementSandboxView View;
        public string UnitId;
        public int ShopSlot=-1;
        public void OnPointerEnter(PointerEventData e){if(ShopSlot>=0)View.HoverShopUnit(ShopSlot);else View.HoverUnit(UnitId);}
        public void OnPointerExit(PointerEventData e){if(ShopSlot>=0)View.EndShopUnitHover(ShopSlot);else View.EndUnitHover(UnitId);}
    }
}

