using UnityEngine.InputSystem;

namespace Boatships
{
    /// <summary>Battle orders for the company's ships (they fight alongside the flagship; see BoatshipsEncounterDirector).</summary>
    public sealed partial class BoatshipsTravelController
    {
        private void CycleCompanyOrders()
        {
            CompanyOrders next = (CompanyOrders)(((int)director.Orders + 1) % 3);
            if (director.SetOrders(next)) Notify("Company orders: " + BoatshipsEncounterDirector.DescribeOrders(next) + ".");
            else Notify("The orders could not be signalled.");
        }

        /// <summary>O in battle signals the next order to the company: engage → cripple → hold back.</summary>
        private void HandleCompanyOrdersKey()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.oKey.wasPressedThisFrame) return;
            if (BoatshipsUI.Typing || BoatshipsDebugMenu.IsOpen || view.MenuOpen || OverlayOpen) return;
            if (economy.CompanySize == 0) { Notify("You have no company ships to signal. Give a captured ship a captain first."); return; }
            CycleCompanyOrders();
        }
    }
}
