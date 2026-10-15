// Taptic Engine feedback for the game (UIKit feedback generators).
// The generators are kept and re-armed after each use: a fresh one per call starts the Taptic
// Engine cold, so quick runs of scope detents could come out late or not at all.
#import <UIKit/UIKit.h>

static UISelectionFeedbackGenerator *selection;
static UIImpactFeedbackGenerator *detent, *heavy, *rigid;
static UINotificationFeedbackGenerator *notice;

extern "C" void SvsHaptic(int kind)
{
    dispatch_async(dispatch_get_main_queue(), ^{
        if (selection == nil)
        {
            selection = [UISelectionFeedbackGenerator new];
            detent = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleRigid];
            heavy = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleHeavy];
            rigid = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleRigid];
            notice = [UINotificationFeedbackGenerator new];
        }
        switch (kind)
        {
            case 0: [selection selectionChanged]; [selection prepare]; break;
            case 1: [heavy impactOccurred]; [heavy prepare]; break;
            case 2: [rigid impactOccurred]; [rigid prepare]; break;
            case 3: [notice notificationOccurred:UINotificationFeedbackTypeError]; [notice prepare]; break;
            case 4: [notice notificationOccurred:UINotificationFeedbackTypeWarning]; [notice prepare]; break;
            case 5: [detent impactOccurredWithIntensity:0.75]; [detent prepare]; break;
        }
    });
}
