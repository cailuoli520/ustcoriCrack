$(document).ready(function () {
    var ss = document.getElementById("loginStatus").value;
    if (_enviroment.Language == "ZH-CN") {
        switch (ss) {
            case "NotYou":
                bootbox.alert("当前用户已在别处登录，请重新登录！");
                break;
            case "NotLogin":
                bootbox.alert("登录失效，请重新登录！");
                break;
            case "NotAuthorization":
                bootbox.alert("没有验证信息，请重新登录！");
                break;
            case "ErrorToken":
                bootbox.alert("密钥错误，请重新登录！");
                break;
            case "OutTime":
                bootbox.alert("登录超时，请重新登录");
                break;
            case "OutArea":
                bootbox.alert("禁止访问未授权区域！");
                break;
        }
    } else {
        switch (ss) {
            case "NotYou":
                bootbox.alert("The current user has logged in elsewhere. Please log in again！");
                break;
            case "NotLogin":
                bootbox.alert("Login failed. Please login again！");
                break;
            case "NotAuthorization":
                bootbox.alert("No authentication information, please login again！");
                break;
            case "ErrorToken":
                bootbox.alert("Wrong key, please login again！");
                break;
            case "OutTime":
                bootbox.alert("Login timeout, please login again");
                break;
            case "OutArea":
                bootbox.alert("Access to unauthorized areas is prohibited！");
                break;
        }
    }

    _InitParams.LABNAME = '';
    _InitParams.LabTypeID = '-999';
});
var base = new Base64();
var ClassInfo = ko.observableArray();
function loginClick() {
    loginValidator();
    $('#loginForm').data('bootstrapValidator').validate();
    if ($("#loginForm").data('bootstrapValidator').isValid() == true) {

        if ($("#checkCode").val() == "") {
            if (_enviroment.Language == "ZH-CN") {
                alert("请输入验证码！");
            } else {
                alert("Please enter the verification code！");
            }
            $("#checkCode").focus();
            return false;
        }

        var uID = hex_hmac_md5("Original", $("#userName").val());
        var uPass = hex_hmac_md5("Original", $("#userPass").val());

        var isSupportNationalVirtual = $("#IsSupportNationalVirtual").val();
        var _nationalVirtualLabName = $("#_nationalVirtualLabName").val();
        var loginURL = _websiteName + '/Login/UserLogin';
        $.ajax({
            type: 'post',
            url: loginURL,
            data: {
                userId: uID,
                userPass: Encrypt3(uPass),
                checkcode: $("#checkCode").val()
            },
            success: function (result) {
                if (result.IsSuccess) {
                    ClassInfo = result;
                    var roleArray = new Array();
                    var urlArray = new Array();

                    _userInfo.UserID = result.Data.UserID;
                    _userInfo.UserName = result.Data.UserName;
                    _userInfo.Token = result.Data.GUID;

                    _userInfo._isSupportOnline = result.Data._isSupportOnline;
                    _userInfo._isSupportPJ = result.Data._isSupportPJ;
                    _userInfo._isSupportAotoLab = result.Data._isSupportAotoLab;
                    _userInfo._isSupportLabDate = result.Data._isSupportLabDate;
                    _userInfo._isSupportReport = result.Data._isSupportReport;
                    _userInfo._isSupportWrite = result.Data._isSupportWrite;
                    _userInfo._isSupportNationalVirtual = result.Data._isSupportNationalVirtual;
                    _userInfo._isAYZ = result.Data._isAYZ;
                    _userInfo._IsUserdDeclare = result.Data._IsUserdDeclare;
                    _userInfo._IsShowpParaScore = result.Data._IsShowpParaScore;
                    _userInfo._isSupportYuXi = result.Data._isSupportYuXi;
                    _userInfo._isSupportStep = result.Data._isSupportStep;
                    _userInfo._StuIsShowLabData = result.Data._StuIsShowLabData;
                    for (var i = 0; i < result.Data.RoleType.length; i++) {
                        roleArray.push(result.Data.RoleType[i].roleType);
                        urlArray.push(result.Data.RoleType[i].Url);
                    }
                    _userInfo.RoleType = roleArray;
                    _userInfo.Areas = urlArray;
                    if (result.ErrorInfo == "-8") {
                        $("#ChangeUserRoleModalLabel").html("您的密码是初始密码，请修改密码：");
                        $('#ChangeUserRoleModal').modal('toggle');
                    }
                    else if (result.ErrorInfo == "-7") {
                        $('#ChangeUserRoleModal').modal('toggle');
                    } else {

                        if (_userInfo._IsUserdDeclare == "false") {//不是申报
                            if (result.ErrorInfo != "") {
                                bootbox.alert({
                                    buttons: {
                                        ok: {
                                            label: '确认',
                                            className: 'btn btn-LightGreen'
                                        }
                                    },
                                    message: result.ErrorInfo,
                                    callback: function () {
                                        _CurrentRoleType.RoleType = result.Data.C_Role;
                                        window.location.href = _websiteName + result.Url;
                                    },
                                    title: "温馨提示"
                                });
                            }
                            else {
                                _CurrentRoleType.RoleType = result.Data.C_Role;
                                window.location.href = _websiteName + result.Url;
                            }

                        } else {//申报用
                            _CurrentRoleType.RoleType = result.Data.C_Role;
                            if (result.Data.RoleType[0].roleType == "1") {
                                window.location.href = _websiteName + "/ReportAdmin/StartPage/StartPage";
                            } else if (result.Data.RoleType[0].roleType == "2") {
                                window.location.href = _websiteName + "/ReportTeacher/TStartPage/TStartPage";
                            } else if (result.Data.RoleType[0].roleType == "3") {
                                window.location.href = _websiteName + "/ReportStudent/SStartPage/SStartPage";
                            } else {
                                window.location.href = _websiteName + result.Url;
                            }
                        }
                    }
                }
                else {
                    bootbox.alert(result.ErrorInfo);
                    CheckCode();
                }
            },
            error: function (data) {
                bootbox.alert(data)
                CheckCode();
            }
        });
    }
}


//保存按钮事件
function SaveTemplete() {
    TempleteValidator();
    $('#TempleteForm').data('bootstrapValidator').validate();
    if ($("#TempleteForm").data('bootstrapValidator').isValid() == true) {
        if ($("#newPassword").val() == $("#againPassword").val()) {
            $.ajax({
                type: 'post',
                url: _websiteName + '/Login/UpdatePass',
                data: {
                    UserID: _userInfo.UserID,
                    OldPassword: $("#oldPassword").val(),
                    NewPassword: $("#newPassword").val()
                },
                success: function (result) {
                    if (result.IsSuccess) {
                        if (_userInfo._IsUserdDeclare == "false") {//不是申报
                            if (result.ErrorInfo != "") {
                                bootbox.alert({
                                    buttons: {
                                        ok: {
                                            label: '确认',
                                            className: 'btn btn-LightGreen'
                                        }
                                    },
                                    message: result.ErrorInfo,
                                    callback: function () {
                                        _CurrentRoleType.RoleType = ClassInfo.Data.C_Role;
                                        window.location.href = _websiteName + ClassInfo.Url;
                                    },
                                    title: "温馨提示"
                                });
                            }
                            else {
                                _CurrentRoleType.RoleType = ClassInfo.Data.C_Role;
                                window.location.href = _websiteName + ClassInfo.Url;
                            }

                        } else {//申报用
                            _CurrentRoleType.RoleType = ClassInfo.Data.C_Role;
                            if (ClassInfo.Data.RoleType[0].roleType == "1") {
                                window.location.href = _websiteName + "/ReportAdmin/StartPage/StartPage";
                            } else if (ClassInfo.Data.RoleType[0].roleType == "2") {
                                window.location.href = _websiteName + "/ReportTeacher/TStartPage/TStartPage";
                            } else if (ClassInfo.Data.RoleType[0].roleType == "3") {
                                window.location.href = _websiteName + "/ReportStudent/SStartPage/SStartPage";
                            } else {
                                window.location.href = _websiteName + ClassInfo.Url;
                            }
                        }
                    } else {
                        $("#oldPassword").val("");
                        $("#newPassword").val("");
                        $("#againPassword").val("");
                        bootbox.alert(result.Data);
                    }

                }
            });
        }
        else {
            bootbox.alert("两次输入密码不一致，请重新输入！");
        }

        $("#TempleteForm").data('bootstrapValidator').destroy();
        $('#TempleteForm').data('bootstrapValidator', null);
    }
}

//输入验证
function TempleteValidator() {
    $('#TempleteForm').bootstrapValidator({
        message: 'This value is not valid',
        feedbackIcons: {
            valid: 'fa fa-check',
            invalid: 'fa fa-remove',
            validating: 'fa fa-refresh'
        },
        fields: {
            oldPassword: {
                message: '原密码无效！',
                validators: {
                    notEmpty: {
                        message: '原密码不能为空！'
                    }
                }
            },
            newPassword: {
                message: '新密码无效！',
                validators: {
                    notEmpty: {
                        message: '新密码不能为空！'
                    },
                    stringLength: {
                        min: 8,
                        max: 20,
                        message: '新密码长度为8-20个字符'
                    },
                    regexp: {
                        regexp: /^(?=.*[0-9])(?=.*[a-zA-Z])(?=.*[^a-zA-Z0-9]).{8,20}/, // /^(?![^a-zA-Z]+$)(?!\D+$)/,   ^(?!\d+$)(?![a-zA-Z]+$)(?![@#$%^&]+$)[\da-zA-Z@#$%^&]+$
                        message: '新密码由字母，数字，特殊字符组成'
                    }
                }
            },
            againPassword: {
                message: '确认密码无效！',
                validators: {
                    notEmpty: {
                        message: '确认密码不能为空！'
                    }
                }
            }
        },
        removeClass: "form-group"
    });
}




//刷新验证码
function CheckCode() {
    var code = document.getElementById('imgCheckCode');
    code.src = _websiteName + '/CheckCode.ashx?Flag=DengLu&aa=' + Math.random();
};

function RedirectUrl(type, SourceName, SourceUrl, length) {
    //debugger;
    _CurrentRoleType.RoleType = ClassInfo.Data.C_Role;
    if (type == 1) {
        window.location.href = _websiteName + ClassInfo.Url;
    }
    else {
        if (ClassInfo.Data.RoleType[0].roleType == "1") {
            var url = _websiteName + "/ReportAdmin/LabSource/LabSource?TopNavID=1&SourceName=" + SourceName + "&SourceUrl=" + SourceUrl;
            window.location.href = url;
        } else if (ClassInfo.Data.RoleType[0].roleType == "2") {
            var url = _websiteName + "/ReportTeacher/TLabSource/TLabSource?TopNavID=1&SourceName=" + SourceName + "&SourceUrl=" + SourceUrl;
            window.location.href = url;
        } else {
            var url = _websiteName + "/ReportStudent/SLabSource/SLabSource?TopNavID=1&SourceName=" + SourceName + "&SourceUrl=" + SourceUrl;
            window.location.href = url;
        }
    }
}

function setCurrentRoleType(date) {
    _CurrentRoleType.RoleType = date;
}

function loginValidator() {
    $('#loginForm').bootstrapValidator({
        message: 'This value is not valid',
        feedbackIcons: {
            valid: 'fa fa-check',
            invalid: 'fa fa-remove',
            validating: 'fa fa-refresh'
        },
        fields: {
            userName: {
                message: '用户名无效！',
                validators: {
                    notEmpty: {
                        message: '用户名不能为空！'
                    },
                    stringLength: {
                        min: 2,
                        max: 15,
                        message: '用户名长度为2-15个字符'
                    },
                    regexp: {
                        regexp: /^[a-zA-Z0-9_]+$/,
                        message: '用户名由字母，下划线、数字组成'
                    }
                }
            },
            password: {
                validators: {
                    notEmpty: {
                        message: '密码不能为空！'
                    },
                    stringLength: {
                        min: 2,
                        max: 20,
                        message: '密码长度为2-20个字符'
                    }
                }
            }
        },
        removeClass: "form-group"
    });
}

function ChangeLanguage() {
    var currentLanguage = $("#languageSpan").text();
    if (currentLanguage == "English") {
        $("#languageSpan").val("中文");
        currentLanguage = "ZH-EN";
    } else {
        $("#languageSpan").val("English");
        currentLanguage = "ZH-CN";
    }
    _enviroment.Language = currentLanguage;
    $.ajax({
        type: 'post',
        url: _websiteName + '/Login/SetLanguage',
        data: {
            Language: currentLanguage
        },
        success: function (result) {
            window.location.reload();
        }
    }); //结束

    //CheckCode();
};

function Encrypt3(text) {
    var key = CryptoJS.enc.Utf8.parse("SAqUg9Z5ldkG3mxL");
    var InDkn4 = CryptoJS.enc.Utf8.parse(text);
    var b = CryptoJS.AES.encrypt(InDkn4, key, {
        mode: CryptoJS.mode.ECB,
        padding: CryptoJS.pad.Pkcs7
    });
    return b["toString"]()
}
