# NiiMotion first-use guide

## 1. Select your devices

On first launch, select only the devices you actually own. NiiMotion orders the available profiles from easiest to most comprehensive. You can change the selection later under **My Devices**.

## 2. Complete base calibration

Every device has its own card under **Test & Calibration**. Open the card, complete the connection steps, then record the three guided five-minute base phases. You can delete and repeat an incorrect phase with its own retry button. A device cannot be used by an active profile until its base phases are complete.

## 3. Select a profile

Open **Change profile** on the Overview page. Normal VR completely disables NiiMotion locomotion. Other profiles can start only while their required devices are ready. Hand tracking never creates locomotion; it is a separate preference for Virtual Desktop controller emulation.

For profiles using two or more motion devices, open **Test & Calibration → Combined Operation Models** and select the combination. Each of the three combined phases lasts two minutes. You can pause, delete and retake a phase. The local fusion model is generated automatically; use **Model Health** to review quality, sample count and backup status.

## 4. Start VR

Turn on the Quest, wait until Virtual Desktop has established a real VR session, then select **Prepare and start VR** in NiiMotion. The app validates the sensors, selects the correct NiiMotion runtime and starts SteamVR as the final step. OpenXR games such as Metro Awakening are not mapped automatically until their adapter is validated.

## 5. Improve the model over time

After base setup, you may use **Improve model with new recordings**. These recordings never erase base calibration. You can reject an unwanted improvement or restore an earlier model from the Recovery Center.
