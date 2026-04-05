namespace ShantiBotDi.Models;

public enum UserAction
{
    None,
    WaitingDepositAmount,
    WaitingWithdrawAmount,
    WaitingInvestAmount,
    WaitingInvestDuration,
    WaitingForUsername,
    WaitingForPassword,
    WaitingForRegistration, // After pressing "I have read"
    WaitingForName,
    WaitingForEmail,
    WaitingForPhone,
    WaitingForAgreement, // Waiting for "I have read"
    WaitingForTermsAgreement,
    WaitingForRegistrationPassword,
    WaitingForRegistrationUsername,
    WaitingForLoginUsername,
    WaitingForLoginPassword,
    WaitingForWalletAddress,
    WaitingForLanguageSelection, // новий стан
    WaitingRegisterUsername,
    WaitingRegisterPassword,
    WaitingRegisterWallet,
    WaitingLoginPassword,
    WaitingRulesAccept,
    WaitingForNewLogin,       // Новий стан
    WaitingForNewPassword,   // Новий стан
    WaitingRegisterBTCWallet,
    WaitingRegisterETHWallet,
    WaitingDepositCurrency,
    WaitingAcknowledgeGuide,
    WaitingBonusTelegramId,
    WaitingBonusActivate,    // Новий стан для активації
    WaitingBonusDeactivate,   // Новий стан для деактивації
    WaitingForNewWalletAddress
    
}